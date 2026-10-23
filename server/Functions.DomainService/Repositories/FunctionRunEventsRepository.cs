using Blocks.Genesis;
using Functions.DomainService.Events;
using Functions.DomainService.Utils;
using MongoDB.Driver;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionRunEventsRepository"/>
    /// <remarks>
    /// Logic-owned collection in the tenant's database (see <see cref="FunctionEventSettingsRepository"/>). Expires with
    /// the run record: <see cref="FunctionsConstants.RunEventsRetention"/> after it was written.
    /// </remarks>
    public class FunctionRunEventsRepository : IFunctionRunEventsRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionRunEventsRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoCollection<FunctionRunEventsEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionRunEventsEntity>(tenantId, FunctionsConstants.FunctionRunEventsCollection);

        private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
        {
            lock (_indexGate)
            {
                if (!_indexedTenants.Add(tenantId)) return;
            }

            var keys = Builders<FunctionRunEventsEntity>.IndexKeys;
            await Collection(tenantId).Indexes.CreateManyAsync(
                [
                    new CreateIndexModel<FunctionRunEventsEntity>(keys.Ascending(d => d.RunId).Descending(d => d.Attempt)),
                    new CreateIndexModel<FunctionRunEventsEntity>(keys.Ascending(d => d.FunctionId)),
                    new CreateIndexModel<FunctionRunEventsEntity>(
                        keys.Ascending(d => d.CreatedDate),
                        new CreateIndexOptions { ExpireAfter = FunctionsConstants.RunEventsRetention }),
                ],
                cancellationToken);
        }

        public async Task<FunctionRunEventsEntity?> GetAsync(
            string tenantId, string runId, int attempt, CancellationToken cancellationToken = default)
        {
            var id = FunctionRunEventsEntity.IdFor(runId, attempt);
            return await Collection(tenantId).Find(d => d.ItemId == id).FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<FunctionRunEventsEntity?> GetLatestAsync(
            string tenantId, string runId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(runId)) return null;

            return await Collection(tenantId)
                .Find(d => d.RunId == runId)
                .SortByDescending(d => d.Attempt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<bool> ExistsAsync(
            string tenantId, string runId, int attempt, CancellationToken cancellationToken = default)
        {
            var id = FunctionRunEventsEntity.IdFor(runId, attempt);
            return await Collection(tenantId).CountDocumentsAsync(
                d => d.ItemId == id, new CountOptions { Limit = 1 }, cancellationToken) > 0;
        }

        public async Task SaveAsync(
            string tenantId, FunctionRunEventsEntity document, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);

            await EnsureIndexesAsync(tenantId, cancellationToken);
            document.ItemId = FunctionRunEventsEntity.IdFor(document.RunId, document.Attempt);
            if (document.CreatedDate == default) document.CreatedDate = DateTime.UtcNow;

            await Collection(tenantId).ReplaceOneAsync(
                d => d.ItemId == document.ItemId, document, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        }

        public async Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return 0;

            var result = await Collection(tenantId).DeleteManyAsync(d => d.FunctionId == functionId, cancellationToken);
            return result.DeletedCount;
        }
    }
}
