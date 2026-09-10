using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Utils;
using MongoDB.Driver;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionRunLogRepository"/>
    public class FunctionRunLogRepository : IFunctionRunLogRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionRunLogRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoCollection<FunctionRunLogEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionRunLogEntity>(tenantId, FunctionsConstants.FunctionRunLogsCollection);

        private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
        {
            lock (_indexGate)
            {
                if (!_indexedTenants.Add(tenantId)) return;
            }

            var keys = Builders<FunctionRunLogEntity>.IndexKeys;
            await Collection(tenantId).Indexes.CreateManyAsync(
                [
                    new CreateIndexModel<FunctionRunLogEntity>(
                        keys.Ascending(l => l.RunId).Ascending(l => l.Seq)),
                    new CreateIndexModel<FunctionRunLogEntity>(
                        keys.Ascending(l => l.Timestamp),
                        new CreateIndexOptions { ExpireAfter = FunctionsConstants.RunLogRetention }),
                ],
                cancellationToken);
        }

        public async Task InsertManyAsync(
            string tenantId, IReadOnlyList<FunctionRunLogEntity> logs, CancellationToken cancellationToken = default)
        {
            if (logs.Count == 0) return;

            await EnsureIndexesAsync(tenantId, cancellationToken);
            // Ordered: preserving the sandbox's own stdout order matters more here than
            // resilience to a single bad document, and a bad document would indicate a bug
            // worth surfacing rather than silently skipping.
            await Collection(tenantId).InsertManyAsync(
                logs, new InsertManyOptions { IsOrdered = true }, cancellationToken);
        }

        public async Task<(IReadOnlyList<FunctionRunLogEntity> Items, long TotalCount)> GetByRunAsync(
            string tenantId, string runId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var filter = Builders<FunctionRunLogEntity>.Filter.Eq(l => l.RunId, runId);
            var collection = Collection(tenantId);

            var totalCount = await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
            var items = await collection.Find(filter)
                .SortBy(l => l.Seq)
                .Skip(Math.Max(0, pageNumber) * Math.Max(1, pageSize))
                .Limit(Math.Max(1, pageSize))
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }
    }
}
