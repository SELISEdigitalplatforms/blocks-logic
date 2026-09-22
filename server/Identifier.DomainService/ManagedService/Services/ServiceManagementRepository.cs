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
        public ServiceManagementRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret )
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        private IMongoDatabase ResolvedClientDb ( )
        {
            var blocksContext = BlocksContext.GetContext() ?? throw new InvalidOperationException("Tenant context is required.");
            if (blocksContext.Impersonated)
            {
                return _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName);
            }
            return _dbContextProvider.GetDatabase(blocksContext.TenantId);
        }

        public async Task<(IQueryable<BlocksManagedService>, long)> GetAllServicesAsync ( GetAllServiceRequest request )
        {
            var database = ResolvedClientDb();
            var collection = database.GetCollection<BlocksManagedService>("BlocksManagedServices");
            var filter = Builders<BlocksManagedService>.Filter.Eq(s => s.TenantId, BlocksContext.GetContext()?.TenantId ?? string.Empty);

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
    }
}
