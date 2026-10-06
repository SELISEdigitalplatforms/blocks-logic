using System.Text.RegularExpressions;
using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Utils;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionRunRepository"/>
    public class FunctionRunRepository : IFunctionRunRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionRunRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoCollection<FunctionRunEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionRunEntity>(tenantId, FunctionsConstants.FunctionRunsCollection);

        private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
        {
            lock (_indexGate)
            {
                if (!_indexedTenants.Add(tenantId)) return;
            }

            var keys = Builders<FunctionRunEntity>.IndexKeys;
            await Collection(tenantId).Indexes.CreateManyAsync(
                [
                    // The list view's default sort: a function's runs, newest first.
                    new CreateIndexModel<FunctionRunEntity>(
                        keys.Ascending(r => r.FunctionId).Descending(r => r.CreatedDate)),
                    new CreateIndexModel<FunctionRunEntity>(keys.Ascending(r => r.Status)),
                    // FunctionStaleRunSweeper: "non-terminal and not touched since...", oldest first.
                    new CreateIndexModel<FunctionRunEntity>(
                        keys.Ascending(r => r.Status).Ascending(r => r.LastUpdatedDate)),
                    // Covers the versions tab's per-version run counts: the group reads the
                    // index rather than every run document of the function.
                    new CreateIndexModel<FunctionRunEntity>(
                        keys.Ascending(r => r.FunctionId).Ascending(r => r.VersionNumber)),
                    // CompletedAt is null until a run finishes, and a TTL index only ever
                    // expires documents whose indexed field holds an actual date — an
                    // in-flight run is never at risk of being swept away mid-execution.
                    new CreateIndexModel<FunctionRunEntity>(
                        keys.Ascending(r => r.CompletedAt),
                        new CreateIndexOptions { ExpireAfter = FunctionsConstants.RunRetention }),
                ],
                cancellationToken);
        }

        public async Task<(IReadOnlyList<FunctionRunEntity> Items, long TotalCount)> GetAllAsync(
            string tenantId, FunctionRunFilter filter, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);

            var builder = Builders<FunctionRunEntity>.Filter;
            var query = builder.Empty;

            if (!string.IsNullOrWhiteSpace(filter.FunctionId))
            {
                query &= builder.Eq(r => r.FunctionId, filter.FunctionId);
            }
            if (filter.ActiveOnly)
            {
                query &= builder.In(r => r.Status, NonTerminalStatuses);
            }
            else if (filter.Status.HasValue)
            {
                query &= builder.Eq(r => r.Status, filter.Status.Value);
            }
            if (filter.FromUtc.HasValue)
            {
                query &= builder.Gte(r => r.CreatedDate, filter.FromUtc.Value);
            }
            if (filter.ToUtc.HasValue)
            {
                query &= builder.Lte(r => r.CreatedDate, filter.ToUtc.Value);
            }
            if (filter.InvokedBy.HasValue)
            {
                query &= builder.Eq(r => r.InvokedBy, filter.InvokedBy.Value);
            }
            if (!string.IsNullOrWhiteSpace(filter.IdPrefix))
            {
                // Anchored so the _id index can serve it; an unanchored regex would scan the
                // whole collection for every keystroke in the runs search box.
                query &= builder.Regex(
                    r => r.ItemId,
                    new BsonRegularExpression("^" + Regex.Escape(filter.IdPrefix.Trim()), "i"));
            }

            var collection = Collection(tenantId);
            var totalCount = await collection.CountDocumentsAsync(query, cancellationToken: cancellationToken);
            // Input (up to 1 MB) and Result (up to 5 MB) are not in RunSummaryDto, so fetching
            // them to build a 20-row table would move up to ~120 MB per page for nothing. An
            // exclusion projection still deserializes into the entity, with those two left null.
            var items = await collection.Find(query)
                .Project<FunctionRunEntity>(Builders<FunctionRunEntity>.Projection
                    .Exclude(r => r.Input)
                    .Exclude(r => r.Result))
                .SortByDescending(r => r.CreatedDate)
                .Skip(Math.Max(0, pageNumber) * Math.Max(1, pageSize))
                .Limit(Math.Max(1, pageSize))
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        private static readonly Enums.RunStatus[] NonTerminalStatuses =
        [
            Enums.RunStatus.Queued, Enums.RunStatus.Claimed, Enums.RunStatus.Starting,
            Enums.RunStatus.Running, Enums.RunStatus.OutputProcessing,
        ];

        public async Task<IReadOnlySet<string>> GetVersionIdsWithActiveRunsAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            var builder = Builders<FunctionRunEntity>.Filter;
            var filter = builder.Eq(r => r.FunctionId, functionId)
                & builder.In(r => r.Status, NonTerminalStatuses)
                & builder.Ne(r => r.VersionId, null);

            var runs = await Collection(tenantId)
                .Find(filter)
                .Project(r => r.VersionId)
                .ToListAsync(cancellationToken);

            return runs.Where(id => !string.IsNullOrEmpty(id)).Select(id => id!)
                .ToHashSet(StringComparer.Ordinal);
        }

        public async Task<IReadOnlyDictionary<int, long>> CountByVersionAsync(
            string tenantId, string functionId, IReadOnlyCollection<int> versionNumbers,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId) || versionNumbers.Count == 0)
            {
                return new Dictionary<int, long>();
            }

            var builder = Builders<FunctionRunEntity>.Filter;
            var query = builder.Eq(r => r.FunctionId, functionId)
                & builder.In(r => r.VersionNumber, versionNumbers);

            var counts = await Collection(tenantId)
                .Aggregate()
                .Match(query)
                .Group(r => r.VersionNumber, g => new { VersionNumber = g.Key, Count = g.LongCount() })
                .ToListAsync(cancellationToken);

            return counts.ToDictionary(c => c.VersionNumber, c => c.Count);
        }

        public async Task<IReadOnlyDictionary<string, long>> CountSinceByFunctionAsync(
            string tenantId, IReadOnlyCollection<string> functionIds, DateTime since,
            CancellationToken cancellationToken = default)
        {
            if (functionIds.Count == 0) return new Dictionary<string, long>();

            var builder = Builders<FunctionRunEntity>.Filter;
            var query = builder.In(r => r.FunctionId, functionIds)
                & builder.Gte(r => r.CreatedDate, since);

            var counts = await Collection(tenantId)
                .Aggregate()
                .Match(query)
                .Group(r => r.FunctionId, g => new { FunctionId = g.Key, Count = g.LongCount() })
                .ToListAsync(cancellationToken);

            return counts.ToDictionary(c => c.FunctionId, c => c.Count);
        }

        public async Task<FunctionRunEntity?> GetByIdAsync(string tenantId, string runId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(runId)) return null;
            return await Collection(tenantId).Find(r => r.ItemId == runId).FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return 0;

            var result = await Collection(tenantId).DeleteManyAsync(r => r.FunctionId == functionId, cancellationToken);
            return result.DeletedCount;
        }

        public async Task CreateAsync(string tenantId, FunctionRunEntity run, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);
            await Collection(tenantId).InsertOneAsync(run, cancellationToken: cancellationToken);
        }


        /// <summary>
        /// Statuses in which a run is still waiting for the runner's outcome of its current
        /// attempt. <c>OUTPUT_PROCESSING</c> is deliberately absent: a run there has already had
        /// its result applied, so a runner result arriving then is a redelivery.
        /// </summary>
        internal static readonly RunStatus[] AwaitingResultStatuses =
        [
            RunStatus.Queued, RunStatus.Claimed, RunStatus.Starting, RunStatus.Running,
        ];

        /// <summary>
        /// Terminal outcomes this platform decided on its own, without hearing from a runner. A
        /// real result for the same attempt is better information than any of them and may
        /// replace them; a runner-reported outcome may never be replaced.
        /// </summary>
        internal static readonly RunErrorCode[] PlatformDeterminedErrorCodes =
        [
            RunErrorCode.Undeliverable, RunErrorCode.EnqueueFailed, RunErrorCode.Abandoned,
        ];

        /// <summary>The filter a result must pass to be written — see <see cref="IFunctionRunRepository.ApplyResultAsync"/>.</summary>
        internal static FilterDefinition<FunctionRunEntity> AcceptsResultFilter(string runId, int attempt)
        {
            var f = Builders<FunctionRunEntity>.Filter;
            return f.Eq(r => r.ItemId, runId)
                & f.Eq(r => r.Attempt, attempt)
                & (f.In(r => r.Status, AwaitingResultStatuses)
                   | (f.In(r => r.Status, TerminalStatuses) & f.In(r => r.ErrorCode, PlatformDeterminedErrorCodes)));
        }

        public async Task<ApplyResultOutcome> ApplyResultAsync(
            string tenantId,
            string runId,
            int attempt,
            RunStatus status,
            RunErrorCode errorCode,
            string? errorMessage,
            string? result,
            int? exitCode,
            long? durationMs,
            long? peakMemoryBytes,
            long? cpuUsageMs,
            string? runnerId,
            DateTime? startedAt,
            DateTime completedAt,
            bool logsTruncated,
            RunSandboxReport? sandbox = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(runId)) return ApplyResultOutcome.NotFound;
            if (attempt < 1) return ApplyResultOutcome.UnknownAttempt;

            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.Status, status)
                .Set(r => r.ErrorCode, errorCode)
                .Set(r => r.ErrorMessage, errorMessage)
                .Set(r => r.Result, result)
                .Set(r => r.ExitCode, exitCode)
                .Set(r => r.DurationMs, durationMs)
                .Set(r => r.PeakMemoryBytes, peakMemoryBytes)
                .Set(r => r.CpuUsageMs, cpuUsageMs)
                .Set(r => r.RunnerId, runnerId)
                .Set(r => r.CompletedAt, completedAt)
                .Set(r => r.LogsTruncated, logsTruncated)
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow)
                .Push(r => r.Attempts, new Models.RunAttempt
                {
                    Number = attempt,
                    Status = status,
                    ErrorCode = errorCode,
                    ErrorMessage = errorMessage,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    DurationMs = durationMs,
                });

            if (startedAt.HasValue)
            {
                update = update.Set(r => r.StartedAt, startedAt.Value);
            }

            // The warm-sandbox report is written only when the runner sent one, so a result from a
            // runner without reuse leaves the run document exactly as before. Unset otherwise, so
            // an attempt served without a report never shows an earlier attempt's values.
            update = sandbox is { } report && (report.Reused is not null || report.DiscardReason is not null || report.HandoverMs is not null)
                ? update
                    .Set(r => r.Reused, report.Reused)
                    .Set(r => r.DiscardReason, report.DiscardReason)
                    .Set(r => r.HandoverMs, report.HandoverMs)
                : update
                    .Unset(r => r.Reused)
                    .Unset(r => r.DiscardReason)
                    .Unset(r => r.HandoverMs);

            // One conditional update: the attempt and status are both what is checked and what
            // is written, so a read-then-write would let two deliveries of the same result both
            // pass the check and push the attempt twice.
            var writeResult = await Collection(tenantId).UpdateOneAsync(
                AcceptsResultFilter(runId, attempt), update, cancellationToken: cancellationToken);
            if (writeResult.ModifiedCount > 0) return ApplyResultOutcome.Applied;

            // Refused. Only now read the record — on the miss path alone — to say why, which the
            // consumer needs to tell a harmless redelivery from a result that should never exist.
            var current = await Collection(tenantId)
                .Find(r => r.ItemId == runId)
                .Project(r => new { r.Attempt })
                .FirstOrDefaultAsync(cancellationToken);

            if (current is null) return ApplyResultOutcome.NotFound;
            if (attempt < current.Attempt) return ApplyResultOutcome.StaleAttempt;
            if (attempt > current.Attempt) return ApplyResultOutcome.UnknownAttempt;
            return ApplyResultOutcome.Duplicate;
        }

        public async Task<bool> ResetForRetryAsync(
            string tenantId, string runId, int attempt, CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.Attempt, attempt)
                .Set(r => r.Status, RunStatus.Queued)
                .Set(r => r.ErrorCode, RunErrorCode.None)
                .Set(r => r.ErrorMessage, (string?)null)
                .Set(r => r.Result, (string?)null)
                .Set(r => r.ExitCode, (int?)null)
                .Set(r => r.DurationMs, (long?)null)
                .Set(r => r.PeakMemoryBytes, (long?)null)
                .Set(r => r.CpuUsageMs, (long?)null)
                .Set(r => r.RunnerId, (string?)null)
                .Set(r => r.StartedAt, (DateTime?)null)
                .Set(r => r.CompletedAt, (DateTime?)null)
                .Set(r => r.LogsTruncated, false)
                .Unset(r => r.Reused)
                .Unset(r => r.DiscardReason)
                .Unset(r => r.HandoverMs)
                .Set(r => r.OutputResults, new List<Models.OutputActionResult>())
                .Set(r => r.IdempotencyKey, $"{runId}-{attempt}")
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow);

            // Only from the attempt before, and only once that attempt has finished: two Worker
            // instances racing the same due entry, or a stale entry, then advance it at most once.
            var f = Builders<FunctionRunEntity>.Filter;
            var filter = f.Eq(r => r.ItemId, runId)
                & f.Eq(r => r.Attempt, attempt - 1)
                & f.In(r => r.Status, TerminalStatuses);

            var result = await Collection(tenantId).UpdateOneAsync(
                filter, update, cancellationToken: cancellationToken);
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// The terminal statuses, as a value the Mongo filter can use. Kept beside the update
        /// rather than derived from <c>FunctionWireMapping.IsTerminal</c> because a filter needs
        /// the set, not a predicate — but the two must list the same statuses, and
        /// <c>FunctionDeadLetterConsumerTests</c> asserts that they do.
        /// </summary>
        internal static readonly RunStatus[] TerminalStatuses =
        [
            RunStatus.Succeeded, RunStatus.Failed, RunStatus.TimedOut,
            RunStatus.Cancelled, RunStatus.ResourceExceeded, RunStatus.OutputFailed,
        ];

        public async Task<bool> FailIfNotTerminalAsync(
            string tenantId,
            string runId,
            RunErrorCode errorCode,
            string errorMessage,
            DateTime completedAt,
            CancellationToken cancellationToken = default)
        {
            // One conditional update, not read-then-write: the status is both the thing being
            // checked and the thing being set, so anything less than an atomic compare-and-set
            // can lose a real result to a stale dead letter.
            var filter = Builders<FunctionRunEntity>.Filter.And(
                Builders<FunctionRunEntity>.Filter.Eq(r => r.ItemId, runId),
                Builders<FunctionRunEntity>.Filter.Nin(r => r.Status, TerminalStatuses));

            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.Status, RunStatus.Failed)
                .Set(r => r.ErrorCode, errorCode)
                .Set(r => r.ErrorMessage, errorMessage)
                .Set(r => r.CompletedAt, completedAt)
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow);

            var result = await Collection(tenantId).UpdateOneAsync(
                filter, update, cancellationToken: cancellationToken);

            // ModifiedCount, not MatchedCount: a run that was already failed with this exact
            // message matches the filter and changes nothing, and reporting that as "this call
            // failed it" would write a duplicate audit record on every redelivery.
            return result.ModifiedCount > 0;
        }

        public Task ApplyStatusOnlyAsync(
            string tenantId, string runId, RunStatus status, CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.Status, status)
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow);
            return Collection(tenantId).UpdateOneAsync(r => r.ItemId == runId, update, cancellationToken: cancellationToken);
        }

        public async Task<bool> TryBeginOutputProcessingAsync(
            string tenantId, string runId, int attempt, CancellationToken cancellationToken = default)
        {
            var f = Builders<FunctionRunEntity>.Filter;
            var filter = f.Eq(r => r.ItemId, runId)
                & f.Eq(r => r.Attempt, attempt)
                & f.Eq(r => r.Status, RunStatus.Succeeded)
                & f.Size(r => r.OutputResults, 0);

            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.Status, RunStatus.OutputProcessing)
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow);

            var result = await Collection(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ApplyOutputResultAsync(
            string tenantId, string runId, int attempt, IReadOnlyList<Models.OutputActionResult> results,
            RunStatus finalStatus, string? errorMessage, CancellationToken cancellationToken = default)
        {
            var f = Builders<FunctionRunEntity>.Filter;
            var filter = f.Eq(r => r.ItemId, runId)
                & f.Eq(r => r.Attempt, attempt)
                & f.Eq(r => r.Status, RunStatus.OutputProcessing);

            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.OutputResults, results.ToList())
                .Set(r => r.Status, finalStatus)
                .Set(r => r.ErrorCode, finalStatus == RunStatus.OutputFailed ? RunErrorCode.OutputActionFailed : RunErrorCode.None)
                .Set(r => r.ErrorMessage, errorMessage)
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow);

            var result = await Collection(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            return result.ModifiedCount > 0;
        }

        public async Task<IReadOnlyList<FunctionRunEntity>> FindStaleCandidatesAsync(
            string tenantId, IReadOnlyCollection<RunStatus> statuses, DateTime updatedBefore,
            DateTime? updatedNotBefore, int limit, CancellationToken cancellationToken = default)
        {
            if (statuses.Count == 0 || limit <= 0) return [];

            var f = Builders<FunctionRunEntity>.Filter;
            var filter = f.In(r => r.Status, statuses) & f.Lt(r => r.LastUpdatedDate, updatedBefore);
            if (updatedNotBefore.HasValue)
            {
                filter &= f.Gte(r => r.LastUpdatedDate, updatedNotBefore.Value);
            }

            return await Collection(tenantId).Find(filter)
                .Project<FunctionRunEntity>(Builders<FunctionRunEntity>.Projection
                    .Exclude(r => r.Input)
                    .Exclude(r => r.Result))
                .SortBy(r => r.LastUpdatedDate)
                .Limit(limit)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> CloseStaleAsync(
            string tenantId, string runId, RunStatus expectedStatus, int expectedAttempt, DateTime expectedLastUpdated,
            RunStatus newStatus, RunErrorCode errorCode, string errorMessage, DateTime completedAt,
            CancellationToken cancellationToken = default)
        {
            // A compare-and-set on everything the sweeper read. LastUpdatedDate is part of it
            // because status and attempt alone cannot see a run that was reset for a retry and
            // re-queued at the same attempt number in between — the date moves on every write.
            var f = Builders<FunctionRunEntity>.Filter;
            var filter = f.Eq(r => r.ItemId, runId)
                & f.Eq(r => r.Status, expectedStatus)
                & f.Eq(r => r.Attempt, expectedAttempt)
                & f.Eq(r => r.LastUpdatedDate, expectedLastUpdated);

            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.Status, newStatus)
                .Set(r => r.ErrorCode, errorCode)
                .Set(r => r.ErrorMessage, errorMessage)
                .Set(r => r.CompletedAt, completedAt)
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow);

            var result = await Collection(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            return result.ModifiedCount > 0;
        }
    }
}
