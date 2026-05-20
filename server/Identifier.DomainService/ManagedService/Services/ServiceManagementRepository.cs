using Amazon.S3.Model;
using Blocks.Genesis;
using DomainService.Shared;
using DomainService.Shared.Entities;
using MongoDB.Driver;

namespace DomainService.ManagedService.Services
{
    public class ServiceManagementRepository : IServiceManagementRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;
        private readonly IMongoDatabase _clientDb;
        public ServiceManagementRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _clientDb = ResolvedClientDb();
        }

        private IMongoDatabase ResolvedClientDb()
        {
            var blocksContext = BlocksContext.GetContext();

            if (blocksContext.Impersonated)
            {
                return _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, "BlocksRootDb");
            }

            return _dbContextProvider.GetDatabase(blocksContext.TenantId);
        }
        public async Task<(IQueryable<BlocksManagedService>, long)> GetAllServicesAsync(GetAllServiceRequest request)
        {
            var collection = _clientDb.GetCollection<BlocksManagedService>("BlocksManagedServices");
            var filter = Builders<BlocksManagedService>.Filter.Eq(s => s.TenantId, request.ProjectKey);

            if (!string.IsNullOrWhiteSpace(request?.Filter?.ServiceName))
            {
                filter &= Builders<BlocksManagedService>.Filter.Eq(s => s.Name, request.Filter?.ServiceName);
            }

            if (!string.IsNullOrWhiteSpace(request?.Filter?.ServiceId))
            {
                filter &= Builders<BlocksManagedService>.Filter.Eq(s => s.ServiceId, request.Filter?.ServiceId);
            }

            var cursor = await collection.Find(filter)
                .Limit(request.PageSize)
                .Skip(request.PageSize * request.Page)
                .ToListAsync();

            var count = await collection.CountDocumentsAsync(filter);

            return (cursor.AsQueryable(), count);
        }

        public async Task SaveAsync(BlocksManagedService service)
        {
            var collection = _clientDb.GetCollection<BlocksManagedService>("BlocksManagedServices");
            await collection.InsertOneAsync(service);
        }
    }
}
