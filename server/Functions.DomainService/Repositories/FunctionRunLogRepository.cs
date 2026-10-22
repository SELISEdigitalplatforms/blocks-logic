using Blocks.Genesis;
using DataAccess.InternalService;
using Functions.DomainService.Utils;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionRunLogRepository"/>
    public class FunctionRunLogRepository : IFunctionRunLogRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionRunLogRepository(IDbContextProvider dbContextProvider, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;
        }
        private readonly ILogicDataAccess _dataAccess;

        private Task<ReadScope<FunctionRunLogEntity>> ReadAsync(string tenantId, FilterDefinition<FunctionRunLogEntity> filter, string callSite, bool useProjection = true) =>
            _dataAccess.ReadAsync(LogicSchemas.FunctionRunLogEntity, filter, $"FunctionRunLogRepository.{callSite}", tenantId, useProjection);


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

            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.FunctionRunLogEntity, "FunctionRunLogRepository.InsertMany", tenantId))
                throw new UnauthorizedAccessException("Function run log create is not allowed by the data access policy.");
            foreach (var log in logs)
                await _dataAccess.PrepareWriteAsync(LogicSchemas.FunctionRunLogEntity, log, isInsert: true, "FunctionRunLogRepository.InsertMany", tenantId);
            await EnsureIndexesAsync(tenantId, cancellationToken);
            // Ordered: preserving the sandbox's own stdout order matters more here than
            // resilience to a single bad document, and a bad document would indicate a bug
            // worth surfacing rather than silently skipping.
            await Collection(tenantId).InsertManyAsync(
                logs, new InsertManyOptions { IsOrdered = true }, cancellationToken);
        }

        public async Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return 0;

            var filter = await _dataAccess.DeleteFilterAsync(LogicSchemas.FunctionRunLogEntity,
                Builders<FunctionRunLogEntity>.Filter.Where(l => l.FunctionId == functionId), "FunctionRunLogRepository.DeleteAllForFunction", tenantId);
            if (filter is null) return 0;
            var result = await Collection(tenantId).DeleteManyAsync(filter, cancellationToken);
            return result.DeletedCount;
        }

        public async Task<(IReadOnlyList<FunctionRunLogEntity> Items, long TotalCount)> GetByRunAsync(
            string tenantId, string runId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var filter = Builders<FunctionRunLogEntity>.Filter.Eq(l => l.RunId, runId);
            var collection = Collection(tenantId);

            var scope = await ReadAsync(tenantId, filter, "GetByRun");
            if (!scope.Granted) return ([], 0);
            var totalCount = await collection.CountDocumentsAsync(scope.Filter, cancellationToken: cancellationToken);
            var items = await scope.Apply(collection)
                .SortBy(l => l.Seq)
                .Skip(Math.Max(0, pageNumber) * Math.Max(1, pageSize))
                .Limit(Math.Max(1, pageSize))
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }
    }
}
