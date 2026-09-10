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

        public async Task CreateAsync(string tenantId, FunctionRunEntity run, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);
            await Collection(tenantId).InsertOneAsync(run, cancellationToken: cancellationToken);
        }


        public async Task<bool> ApplyResultAsync(
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
            string? runnerId,
            DateTime? startedAt,
            DateTime completedAt,
            bool logsTruncated,
            CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.Status, status)
                .Set(r => r.ErrorCode, errorCode)
                .Set(r => r.ErrorMessage, errorMessage)
                .Set(r => r.Result, result)
                .Set(r => r.ExitCode, exitCode)
                .Set(r => r.DurationMs, durationMs)
                .Set(r => r.PeakMemoryBytes, peakMemoryBytes)
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

            var writeResult = await Collection(tenantId).UpdateOneAsync(
                r => r.ItemId == runId, update, cancellationToken: cancellationToken);
            return writeResult.MatchedCount > 0;
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
                .Set(r => r.RunnerId, (string?)null)
                .Set(r => r.StartedAt, (DateTime?)null)
                .Set(r => r.CompletedAt, (DateTime?)null)
                .Set(r => r.LogsTruncated, false)
                .Set(r => r.IdempotencyKey, $"{runId}-{attempt}")
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow);

            var result = await Collection(tenantId).UpdateOneAsync(
                r => r.ItemId == runId, update, cancellationToken: cancellationToken);
            return result.MatchedCount > 0;
        }

        public Task ApplyStatusOnlyAsync(
            string tenantId, string runId, RunStatus status, CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.Status, status)
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow);
            return Collection(tenantId).UpdateOneAsync(r => r.ItemId == runId, update, cancellationToken: cancellationToken);
        }

        public Task ApplyOutputResultAsync(
            string tenantId, string runId, IReadOnlyList<Models.OutputActionResult> results,
            RunStatus finalStatus, string? errorMessage, CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionRunEntity>.Update
                .Set(r => r.OutputResults, results.ToList())
                .Set(r => r.Status, finalStatus)
                .Set(r => r.ErrorCode, finalStatus == RunStatus.OutputFailed ? RunErrorCode.OutputActionFailed : RunErrorCode.None)
                .Set(r => r.ErrorMessage, errorMessage)
                .Set(r => r.LastUpdatedDate, DateTime.UtcNow);
            return Collection(tenantId).UpdateOneAsync(r => r.ItemId == runId, update, cancellationToken: cancellationToken);
        }
    }
}
