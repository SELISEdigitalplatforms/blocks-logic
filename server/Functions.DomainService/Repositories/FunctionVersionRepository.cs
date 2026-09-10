using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Utils;
using MongoDB.Driver;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionVersionRepository"/>
    public class FunctionVersionRepository : IFunctionVersionRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionVersionRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

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
            var totalCount = await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
            // Each version snapshots its own full copy of the source, and the versions table
            // shows only number, digest, note and author. GetVersionSourceAsync is the one path
            // that wants the code, and it fetches a single version by id.
            var items = await collection.Find(filter)
                .Project<FunctionVersionEntity>(Builders<FunctionVersionEntity>.Projection
                    .Exclude(v => v.Source)
                    .Exclude(v => v.Packages))
                .SortByDescending(v => v.Number)
                .Skip(Math.Max(0, pageNumber) * Math.Max(1, pageSize))
                .Limit(Math.Max(1, pageSize))
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<FunctionVersionEntity?> GetByIdAsync(string tenantId, string versionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(versionId)) return null;
            return await Collection(tenantId).Find(v => v.ItemId == versionId).FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<FunctionVersionEntity?> GetByNumberAsync(
            string tenantId, string functionId, int number, CancellationToken cancellationToken = default)
        {
            var filter = Builders<FunctionVersionEntity>.Filter.Eq(v => v.FunctionId, functionId)
                       & Builders<FunctionVersionEntity>.Filter.Eq(v => v.Number, number);
            return await Collection(tenantId).Find(filter).FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<FunctionVersionEntity?> GetLatestAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            var filter = Builders<FunctionVersionEntity>.Filter.Eq(v => v.FunctionId, functionId);
            return await Collection(tenantId).Find(filter)
                .SortByDescending(v => v.Number)
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task CreateAsync(string tenantId, FunctionVersionEntity version, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);
            await Collection(tenantId).InsertOneAsync(version, cancellationToken: cancellationToken);
        }

        public async Task<IReadOnlyList<FunctionVersionEntity>> GetAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            return await Collection(tenantId)
                .Find(v => v.FunctionId == functionId)
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

            var result = await Collection(tenantId).DeleteManyAsync(
                Builders<FunctionVersionEntity>.Filter.In(v => v.ItemId, versionIds), cancellationToken);
            return result.DeletedCount;
        }

        public async Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            var result = await Collection(tenantId).DeleteManyAsync(
                v => v.FunctionId == functionId, cancellationToken);
            return result.DeletedCount;
        }
    }
}
