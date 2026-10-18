using Blocks.Genesis;
using DataAccess.InternalService;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionRepository"/>
    public class FunctionRepository : IFunctionRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<FunctionRepository> _logger;

        // Indexes are ensured once per tenant database, not once per process: each tenant is a
        // separate Mongo database, so a single flag on the (singleton) repository instance
        // would only ever cover the first tenant it happened to see.
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        private readonly ILogicDataAccess _dataAccess;

        public FunctionRepository(IDbContextProvider dbContextProvider, ILogger<FunctionRepository> logger, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
            _dataAccess = dataAccess;
        }

        private Task<ReadScope<FunctionEntity>> ReadAsync(string tenantId, FilterDefinition<FunctionEntity> filter, string callSite, bool useProjection = true) =>
            _dataAccess.ReadAsync(LogicSchemas.FunctionEntity, filter, $"FunctionRepository.{callSite}", tenantId, useProjection);

        private Task<FilterDefinition<FunctionEntity>?> EditAsync(string tenantId, FilterDefinition<FunctionEntity> filter, string callSite) =>
            _dataAccess.EditFilterAsync(LogicSchemas.FunctionEntity, filter, $"FunctionRepository.{callSite}", tenantId);

        private IMongoCollection<FunctionEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionEntity>(tenantId, FunctionsConstants.FunctionsCollection);

        /// <summary>
        /// A live function: no tombstone. Eq-null also matches documents written before the field
        /// existed. Every read and write below goes through this, so a function whose delete has
        /// been accepted is invisible — to the list, the editor, deploys, the workflow step and
        /// the public route alike — while the background purge is still working through it.
        /// </summary>
        private static FilterDefinition<FunctionEntity> Live(string functionId) =>
            Builders<FunctionEntity>.Filter.Eq(f => f.ItemId, functionId) & NotDeleted;

        private static readonly FilterDefinition<FunctionEntity> NotDeleted =
            Builders<FunctionEntity>.Filter.Eq(f => f.Deletion, null);

        private static readonly FilterDefinition<FunctionEntity> IsDeleted =
            Builders<FunctionEntity>.Filter.Ne(f => f.Deletion, null);

        private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
        {
            lock (_indexGate)
            {
                if (!_indexedTenants.Add(tenantId)) return;
            }

            // A function is addressed by its ItemId everywhere — the management API, the
            // workflow node and the public /api/fn/{functionId} route — so there is no
            // second name to keep unique and _id already covers identity.
            var keys = Builders<FunctionEntity>.IndexKeys;
            await Collection(tenantId).Indexes.CreateManyAsync(
                [
                    new CreateIndexModel<FunctionEntity>(keys.Ascending(f => f.Status)),
                    // The list's two sorts. Without these Mongo sorts the whole matched set in
                    // memory, which is fine for a handful of functions and fails at the 32 MB
                    // sort limit for a tenant with many.
                    new CreateIndexModel<FunctionEntity>(keys.Descending(f => f.LastUpdatedDate)),
                    new CreateIndexModel<FunctionEntity>(keys.Ascending(f => f.Name)),
                    // The backstop sweep's "tombstones, oldest first". Sparse: live functions,
                    // which are nearly all of them, carry no entry.
                    new CreateIndexModel<FunctionEntity>(
                        keys.Ascending("Deletion.RequestedAt"),
                        new CreateIndexOptions { Sparse = true }),
                ],
                cancellationToken);
        }

        /// <summary>
        /// The list's sort choice. Only fields of the function itself are sortable, so one query
        /// still serves the page — "last run" lives in the run-stats collection and would need a
        /// lookup. Anything unrecognised (including null) keeps the default, newest-updated first.
        /// </summary>
        public static SortDefinition<FunctionEntity> SortFor(string? sortBy) =>
            string.Equals(sortBy, "Name", StringComparison.OrdinalIgnoreCase)
                ? Builders<FunctionEntity>.Sort.Ascending(f => f.Name)
                : Builders<FunctionEntity>.Sort.Descending(f => f.LastUpdatedDate);

        public async Task<(IReadOnlyList<FunctionEntity> Items, long TotalCount)> GetAllAsync(
            string tenantId, string? searchKey, string? status, string? sortBy,
            int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);

            var builder = Builders<FunctionEntity>.Filter;
            var filter = NotDeleted;

            if (!string.IsNullOrWhiteSpace(searchKey))
            {
                filter &= builder.Regex(f => f.Name, new BsonRegularExpression(searchKey, "i"));
            }
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<FunctionStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                filter &= builder.Eq(f => f.Status, parsedStatus);
            }

            var collection = Collection(tenantId);
            // Own projection (no Source) is kept for the list; only the resolver row filter is added.
            var scope = await ReadAsync(tenantId, filter, "GetAll", useProjection: false);
            if (!scope.Granted) return ([], 0);
            filter = scope.Filter;
            var totalCount = await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
            // The list shows name, status, version and counters — never the code. Source carries
            // index.js (up to 2 MB), package.json and the lockfile, so projecting it away keeps
            // a page of the list small regardless of how large the tenants' functions are.
            var sort = SortFor(sortBy);

            var items = await collection.Find(filter)
                .Project<FunctionEntity>(Builders<FunctionEntity>.Projection.Exclude(f => f.Source))
                .Sort(sort)
                .Skip(Math.Max(0, pageNumber) * Math.Max(1, pageSize))
                .Limit(Math.Max(1, pageSize))
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<FunctionEntity?> GetByIdAsync(string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return null;
            var scope = await ReadAsync(tenantId, Live(functionId), "GetById");
            return scope.Granted ? await scope.Apply(Collection(tenantId)).FirstOrDefaultAsync(cancellationToken) : null;
        }

        public async Task CreateAsync(string tenantId, FunctionEntity function, CancellationToken cancellationToken = default)
        {
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.FunctionEntity, "FunctionRepository.Create", tenantId))
                throw new UnauthorizedAccessException("Function create is not allowed by the data access policy.");
            await _dataAccess.PrepareWriteAsync(LogicSchemas.FunctionEntity, function, isInsert: true, "FunctionRepository.Create", tenantId);
            await EnsureIndexesAsync(tenantId, cancellationToken);
            await Collection(tenantId).InsertOneAsync(function, cancellationToken: cancellationToken);
        }

        public Task UpdateDetailsAsync(
            string tenantId, string functionId, string name, string? description, string? actorId,
            CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionEntity>.Update
                .Set(f => f.Name, name)
                .Set(f => f.Description, description);
            return ApplyAsync(tenantId, functionId, update, actorId, cancellationToken);
        }

        public Task UpdateSourceAndConfigAsync(
            string tenantId, string functionId, FunctionSource source, string sourceHash,
            FunctionLimits limits, RetryPolicy retry, TriggerConfig trigger,
            List<OutputAction> outputActions, List<VariableBinding> variables, string? actorId,
            CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionEntity>.Update
                .Set(f => f.Source, source)
                .Set(f => f.SourceHash, sourceHash)
                .Set(f => f.Limits, limits)
                .Set(f => f.Retry, retry)
                .Set(f => f.Trigger, trigger)
                .Set(f => f.OutputActions, outputActions)
                .Set(f => f.Variables, variables);
            // IsDirty is deliberately [BsonIgnore] — it is derived by comparing the working
            // source hash against the active version's, in FunctionService.IsDirty. Setting it
            // here asked the driver to write a field that has no BSON mapping, which threw
            // "Expression not supported: f.IsDirty" and turned every Save into a 500.
            return ApplyAsync(tenantId, functionId, update, actorId, cancellationToken);
        }

        public Task SetActiveVersionAsync(
            string tenantId, string functionId, string versionId, int versionNumber, DateTime deployedAt,
            string? actorId, CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionEntity>.Update
                .Set(f => f.ActiveVersionId, versionId)
                .Set(f => f.LastVersionNumber, versionNumber)
                .Set(f => f.Status, FunctionStatus.Live)
                .Set(f => f.LastDeployedAt, deployedAt);
            // Nothing to clear: what was just deployed matches the working copy, so the derived
            // IsDirty is false on its own. (Same reason as SaveSourceAsync above.)
            return ApplyAsync(tenantId, functionId, update, actorId, cancellationToken);
        }

        public Task MoveActiveVersionAsync(
            string tenantId, string functionId, string versionId, string? actorId,
            CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionEntity>.Update
                .Set(f => f.ActiveVersionId, versionId)
                .Set(f => f.Status, FunctionStatus.Live);
            return ApplyAsync(tenantId, functionId, update, actorId, cancellationToken);
        }

        /// <summary>Stamps who and when, then applies one targeted update.</summary>
        private async Task ApplyAsync(
            string tenantId, string functionId, UpdateDefinition<FunctionEntity> update, string? actorId,
            CancellationToken cancellationToken)
        {
            update = Builders<FunctionEntity>.Update.Combine(
                update,
                Builders<FunctionEntity>.Update.Set(f => f.LastUpdatedDate, DateTime.UtcNow),
                Builders<FunctionEntity>.Update.Set(f => f.LastUpdatedBy, actorId ?? string.Empty));

            var filter = await EditAsync(tenantId, Live(functionId), "Apply");
            if (filter is null) return;
            await _dataAccess.CheckUpdateAsync(LogicSchemas.FunctionEntity, update, "FunctionRepository.Apply", tenantId);
            await Collection(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        }

        public async Task RecordRunStartedAsync(
            string tenantId, string functionId, DateTime startedAt, CancellationToken cancellationToken = default)
        {
            try
            {
                var update = Builders<FunctionEntity>.Update
                    .Inc(f => f.TotalRuns, 1)
                    .Max(f => f.LastRunAt, startedAt);
                // Hot path: every run start. No-context / other-tenant calls skip inside the helper.
                var filter = await EditAsync(tenantId, Live(functionId), "RecordRunStarted");
                if (filter is null) return;
                await Collection(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to count a run for function {FunctionId}", functionId);
            }
        }

        public async Task ImportRunCountersAsync(
            string tenantId, string functionId, long totalRuns, DateTime? lastRunAt,
            CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionEntity>.Update.Inc(f => f.TotalRuns, Math.Max(0, totalRuns));
            if (lastRunAt is { } last) update = update.Max(f => f.LastRunAt, last);
            // Tombstoned functions included: harmless, and the document goes with the purge.
            var filter = await EditAsync(tenantId, Builders<FunctionEntity>.Filter.Eq(f => f.ItemId, functionId), "ImportRunCounters");
            if (filter is null) return;
            await Collection(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        }

        public async Task<bool> MarkDeletedAsync(
            string tenantId, string functionId, FunctionDeletion deletion, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return false;
            await EnsureIndexesAsync(tenantId, cancellationToken);

            // Conditional on there being no tombstone yet: the second of two concurrent deletes
            // matches nothing and reports "not found" instead of stamping a second request.
            var update = Builders<FunctionEntity>.Update
                .Set(f => f.Deletion, deletion)
                .Set(f => f.LastUpdatedDate, deletion.RequestedAt)
                .Set(f => f.LastUpdatedBy, deletion.RequestedBy ?? string.Empty);
            var filter = await EditAsync(tenantId, Live(functionId), "MarkDeleted");
            if (filter is null) return false;
            await _dataAccess.CheckUpdateAsync(LogicSchemas.FunctionEntity, update, "FunctionRepository.MarkDeleted", tenantId);
            var result = await Collection(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            return result.ModifiedCount > 0;
        }

        public async Task<FunctionEntity?> GetDeletedAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return null;
            var scope = await ReadAsync(tenantId, Builders<FunctionEntity>.Filter.Eq(f => f.ItemId, functionId) & IsDeleted, "GetDeleted", useProjection: false);
            if (!scope.Granted) return null;
            return await Collection(tenantId)
                .Find(scope.Filter)
                .Project<FunctionEntity>(Builders<FunctionEntity>.Projection.Exclude(f => f.Source))
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<string>> GetDeletedIdsAsync(
            string tenantId, int limit, CancellationToken cancellationToken = default)
        {
            var scope = await ReadAsync(tenantId, IsDeleted, "GetDeletedIds", useProjection: false);
            if (!scope.Granted) return [];
            return await Collection(tenantId)
                .Find(scope.Filter)
                .Sort(Builders<FunctionEntity>.Sort.Ascending("Deletion.RequestedAt"))
                .Limit(Math.Max(1, limit))
                .Project(f => f.ItemId)
                .ToListAsync(cancellationToken);
        }

        public async Task RecordPurgePassAsync(
            string tenantId, string functionId, Services.FunctionPurgeReport report, DateTime at,
            CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionEntity>.Update
                .Inc(f => f.Deletion!.Passes, 1)
                .Inc(f => f.Deletion!.Versions, report.Versions)
                .Inc(f => f.Deletion!.Builds, report.Builds)
                .Inc(f => f.Deletion!.Runs, report.Runs)
                .Inc(f => f.Deletion!.RunLogs, report.RunLogs)
                .Inc(f => f.Deletion!.ImagesReleased, (long)report.ImagesReleased)
                .Inc(f => f.Deletion!.RunsCancelled, (long)report.RunsCancelled)
                .Set(f => f.Deletion!.LastPassAt, at);
            var filter = await EditAsync(tenantId, Builders<FunctionEntity>.Filter.Eq(f => f.ItemId, functionId) & IsDeleted, "RecordPurgePass");
            if (filter is null) return;
            await _dataAccess.CheckUpdateAsync(LogicSchemas.FunctionEntity, update, "FunctionRepository.RecordPurgePass", tenantId);
            await Collection(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        }

        public async Task<bool> DeleteTombstoneAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return false;
            var filter = await _dataAccess.DeleteFilterAsync(LogicSchemas.FunctionEntity,
                Builders<FunctionEntity>.Filter.Eq(f => f.ItemId, functionId) & IsDeleted, "FunctionRepository.DeleteTombstone", tenantId);
            if (filter is null) return false;
            var result = await Collection(tenantId).DeleteOneAsync(filter, cancellationToken);
            return result.DeletedCount > 0;
        }
    }
}
