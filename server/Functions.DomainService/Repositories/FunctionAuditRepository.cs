using Blocks.Genesis;
using DataAccess.InternalService;
using Functions.DomainService.Utils;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionAuditRepository"/>
    public class FunctionAuditRepository : IFunctionAuditRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionAuditRepository(IDbContextProvider dbContextProvider, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;
        }
        private readonly ILogicDataAccess _dataAccess;

        private Task<ReadScope<FunctionAuditEventEntity>> ReadAsync(string tenantId, FilterDefinition<FunctionAuditEventEntity> filter, string callSite, bool useProjection = true) =>
            _dataAccess.ReadAsync(LogicSchemas.FunctionAuditEventEntity, filter, $"FunctionAuditRepository.{callSite}", tenantId, useProjection);


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
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.FunctionAuditEventEntity, "FunctionAuditRepository.Create", tenantId))
                throw new UnauthorizedAccessException("Function audit event create is not allowed by the data access policy.");
            await EnsureIndexesAsync(tenantId, cancellationToken);
            await Collection(tenantId).InsertOneAsync(auditEvent, cancellationToken: cancellationToken);
        }

        public async Task<(IReadOnlyList<FunctionAuditEventEntity> Items, long TotalCount)> GetByFunctionAsync(
            string tenantId, string functionId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var filter = Builders<FunctionAuditEventEntity>.Filter.Eq(a => a.FunctionId, functionId);
            var collection = Collection(tenantId);

            var scope = await ReadAsync(tenantId, filter, "GetByFunction");
            if (!scope.Granted) return ([], 0);
            var totalCount = await collection.CountDocumentsAsync(scope.Filter, cancellationToken: cancellationToken);
            var items = await scope.Apply(collection)
                .SortByDescending(a => a.CreatedDate)
                .Skip(Math.Max(0, pageNumber) * Math.Max(1, pageSize))
                .Limit(Math.Max(1, pageSize))
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }
    }
}
