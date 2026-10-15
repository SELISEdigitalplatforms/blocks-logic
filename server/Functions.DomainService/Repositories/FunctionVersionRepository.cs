using Blocks.Genesis;
using DataAccess.InternalService;
using Functions.DomainService.Utils;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionVersionRepository"/>
    public class FunctionVersionRepository : IFunctionVersionRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionVersionRepository(IDbContextProvider dbContextProvider, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;
        }
        private readonly ILogicDataAccess _dataAccess;

        private Task<ReadScope<FunctionVersionEntity>> ReadAsync(string tenantId, FilterDefinition<FunctionVersionEntity> filter, string callSite, bool useProjection = true) =>
            _dataAccess.ReadAsync(LogicSchemas.FunctionVersionEntity, filter, $"FunctionVersionRepository.{callSite}", tenantId, useProjection);


        private IMongoCollection<FunctionVersionEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionVersionEntity>(tenantId, FunctionsConstants.FunctionVersionsCollection);

        private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
        {
            lock (_indexGate)
            {
                if (!_indexedTenants.Add(tenantId)) return;
            }

            var keys = Builders<FunctionVersionEntity>.IndexKeys;
            await Collection(tenantId).Indexes.CreateManyAsync(
                [
                    new CreateIndexModel<FunctionVersionEntity>(
                        keys.Ascending(v => v.FunctionId).Descending(v => v.Number),
                        new CreateIndexOptions { Unique = true }),
                ],
                cancellationToken);
        }

        public async Task<(IReadOnlyList<FunctionVersionEntity> Items, long TotalCount)> GetAllAsync(
            string tenantId, string functionId, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);

            var filter = Builders<FunctionVersionEntity>.Filter.Eq(v => v.FunctionId, functionId);
            var collection = Collection(tenantId);
            // Own projection (no Source) is kept for the list; only the resolver row filter is added.
            var scope = await ReadAsync(tenantId, filter, "GetAll", useProjection: false);
            if (!scope.Granted) return ([], 0);
            filter = scope.Filter;
            var totalCount = await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
            // Each version snapshots its own full copy of the source, which the versions table never
            // shows. GetVersionSourceAsync is the one path that wants the code, and it fetches a single
            // version by id. Packages stays: it is a short summary and the table's Packages column.
            var items = await collection.Find(filter)
                .Project<FunctionVersionEntity>(Builders<FunctionVersionEntity>.Projection
                    .Exclude(v => v.Source))
                .SortByDescending(v => v.Number)
                .Skip(Math.Max(0, pageNumber) * Math.Max(1, pageSize))
                .Limit(Math.Max(1, pageSize))
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<FunctionVersionEntity?> GetByIdAsync(string tenantId, string versionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(versionId)) return null;
            var scope = await ReadAsync(tenantId, Builders<FunctionVersionEntity>.Filter.Where(v => v.ItemId == versionId), "GetById");
            return scope.Granted ? await scope.Apply(Collection(tenantId)).FirstOrDefaultAsync(cancellationToken) : null;
        }

        public async Task<FunctionVersionEntity?> GetByNumberAsync(
            string tenantId, string functionId, int number, CancellationToken cancellationToken = default)
        {
            var filter = Builders<FunctionVersionEntity>.Filter.Eq(v => v.FunctionId, functionId)
                       & Builders<FunctionVersionEntity>.Filter.Eq(v => v.Number, number);
            var scope = await ReadAsync(tenantId, filter, "GetByNumber");
            return scope.Granted ? await scope.Apply(Collection(tenantId)).FirstOrDefaultAsync(cancellationToken) : null;
        }

        public async Task<FunctionVersionEntity?> GetLatestAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            var filter = Builders<FunctionVersionEntity>.Filter.Eq(v => v.FunctionId, functionId);
            var scope = await ReadAsync(tenantId, filter, "GetLatest");
            if (!scope.Granted) return null;
            return await scope.Apply(Collection(tenantId))
                .SortByDescending(v => v.Number)
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task CreateAsync(string tenantId, FunctionVersionEntity version, CancellationToken cancellationToken = default)
        {
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.FunctionVersionEntity, "FunctionVersionRepository.Create", tenantId))
                throw new UnauthorizedAccessException("Function version create is not allowed by the data access policy.");
            await EnsureIndexesAsync(tenantId, cancellationToken);
            await Collection(tenantId).InsertOneAsync(version, cancellationToken: cancellationToken);
        }

        public async Task<IReadOnlyList<FunctionVersionEntity>> GetAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            var scope = await ReadAsync(tenantId, Builders<FunctionVersionEntity>.Filter.Where(v => v.FunctionId == functionId), "GetAllForFunction", useProjection: false);
            if (!scope.Granted) return [];
            return await Collection(tenantId)
                .Find(scope.Filter)
                .Project<FunctionVersionEntity>(Builders<FunctionVersionEntity>.Projection
                    .Exclude(v => v.Source)
                    .Exclude(v => v.Packages))
                .SortByDescending(v => v.Number)
                .ToListAsync(cancellationToken);
        }

        public async Task<long> DeleteManyAsync(
            string tenantId, IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken = default)
        {
            if (versionIds.Count == 0) return 0;

            var filter = await _dataAccess.DeleteFilterAsync(LogicSchemas.FunctionVersionEntity,
                Builders<FunctionVersionEntity>.Filter.In(v => v.ItemId, versionIds), "FunctionVersionRepository.DeleteMany", tenantId);
            if (filter is null) return 0;
            var result = await Collection(tenantId).DeleteManyAsync(filter, cancellationToken);
            return result.DeletedCount;
        }

        public async Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            var filter = await _dataAccess.DeleteFilterAsync(LogicSchemas.FunctionVersionEntity,
                Builders<FunctionVersionEntity>.Filter.Where(v => v.FunctionId == functionId), "FunctionVersionRepository.DeleteAllForFunction", tenantId);
            if (filter is null) return 0;
            var result = await Collection(tenantId).DeleteManyAsync(filter, cancellationToken);
            return result.DeletedCount;
        }
    }
}
