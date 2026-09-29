using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Utils;
using MongoDB.Driver;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionAuditRepository"/>
    public class FunctionAuditRepository : IFunctionAuditRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionAuditRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoCollection<FunctionAuditEventEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionAuditEventEntity>(tenantId, FunctionsConstants.FunctionAuditEventsCollection);

        private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
        {
            lock (_indexGate)
            {
                if (!_indexedTenants.Add(tenantId)) return;
            }

            var keys = Builders<FunctionAuditEventEntity>.IndexKeys;
            await Collection(tenantId).Indexes.CreateManyAsync(
                [
                    new CreateIndexModel<FunctionAuditEventEntity>(
                        keys.Ascending(a => a.FunctionId).Descending(a => a.CreatedDate)),
                    new CreateIndexModel<FunctionAuditEventEntity>(
                        keys.Ascending(a => a.CreatedDate),
                        new CreateIndexOptions { ExpireAfter = FunctionsConstants.AuditRetention }),
                ],
                cancellationToken);
        }

        public async Task CreateAsync(
            string tenantId, FunctionAuditEventEntity auditEvent, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);
            await Collection(tenantId).InsertOneAsync(auditEvent, cancellationToken: cancellationToken);
        }

        public async Task<(IReadOnlyList<FunctionAuditEventEntity> Items, long TotalCount)> GetByFunctionAsync(
            string tenantId, string functionId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var filter = Builders<FunctionAuditEventEntity>.Filter.Eq(a => a.FunctionId, functionId);
            var collection = Collection(tenantId);

            var totalCount = await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
            var items = await collection.Find(filter)
                .SortByDescending(a => a.CreatedDate)
                .Skip(Math.Max(0, pageNumber) * Math.Max(1, pageSize))
                .Limit(Math.Max(1, pageSize))
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }
    }
}
