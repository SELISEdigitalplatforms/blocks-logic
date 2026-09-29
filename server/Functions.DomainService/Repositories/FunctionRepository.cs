using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

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

        public FunctionRepository(IDbContextProvider dbContextProvider, ILogger<FunctionRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
        }

        private IMongoCollection<FunctionEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionEntity>(tenantId, FunctionsConstants.FunctionsCollection);

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
            var filter = builder.Empty;

            if (!string.IsNullOrWhiteSpace(searchKey))
            {
                filter &= builder.Regex(f => f.Name, new BsonRegularExpression(searchKey, "i"));
            }
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<Enums.FunctionStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                filter &= builder.Eq(f => f.Status, parsedStatus);
            }

            var collection = Collection(tenantId);
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
            return await Collection(tenantId)
                .Find(f => f.ItemId == functionId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task CreateAsync(string tenantId, FunctionEntity function, CancellationToken cancellationToken = default)
        {
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
                .Set(f => f.Status, Enums.FunctionStatus.Live)
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
                .Set(f => f.Status, Enums.FunctionStatus.Live);
            return ApplyAsync(tenantId, functionId, update, actorId, cancellationToken);
        }

        /// <summary>Stamps who and when, then applies one targeted update.</summary>
        private Task ApplyAsync(
            string tenantId, string functionId, UpdateDefinition<FunctionEntity> update, string? actorId,
            CancellationToken cancellationToken)
        {
            update = Builders<FunctionEntity>.Update.Combine(
                update,
                Builders<FunctionEntity>.Update.Set(f => f.LastUpdatedDate, DateTime.UtcNow),
                Builders<FunctionEntity>.Update.Set(f => f.LastUpdatedBy, actorId ?? string.Empty));

            return Collection(tenantId).UpdateOneAsync(
                f => f.ItemId == functionId, update, cancellationToken: cancellationToken);
        }

        public async Task<bool> DeleteAsync(string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await Collection(tenantId).DeleteOneAsync(f => f.ItemId == functionId, cancellationToken);
                return result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete function {FunctionId} for tenant {TenantId}", functionId, tenantId);
                return false;
            }
        }
    }
}
