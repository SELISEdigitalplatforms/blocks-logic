using Blocks.Genesis;
using DomainService.Shared;
using DomainService.Shared.Entities;
using MongoDB.Bson;
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

        private const string ManagedServicesCollection = "BlocksManagedServices";

        // Impersonated: root (as before). Not impersonated: the tenant DB only while it still has
        // BlocksManagedServices (live check), else root (the collection moved there).
        private async Task<IMongoDatabase> ResolvedClientDbAsync ( )
        {
            var blocksContext = BlocksContext.GetContext() ?? throw new InvalidOperationException("Tenant context is required.");
            if (blocksContext.Impersonated)
            {
                return RootDb();
            }

            var tenantDb = _dbContextProvider.GetDatabase(blocksContext.TenantId);
            using var names = await tenantDb.ListCollectionNamesAsync(new ListCollectionNamesOptions
            {
                Filter = new BsonDocument("name", ManagedServicesCollection)
            });

            return await names.AnyAsync() ? tenantDb : RootDb();
        }

        private IMongoDatabase RootDb ( )
            => _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName);

        public async Task<(IQueryable<BlocksManagedService>, long)> GetAllServicesAsync ( GetAllServiceRequest request )
        {
            var database = await ResolvedClientDbAsync();
            var collection = database.GetCollection<BlocksManagedService>(ManagedServicesCollection);
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
