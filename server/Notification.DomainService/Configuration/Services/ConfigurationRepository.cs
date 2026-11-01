using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Os;

namespace DomainService.Configuration.Services
{
    public class ConfigurationRepository : IConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "NotificationConfigurations";
        private readonly IBlocksSecret _blocksSecret;
        private const string NotificationKeyPrefix = "notification-config:";
        private readonly ILogger<ConfigurationRepository> _logger;
        private readonly IKeyValueStore _keyValueStore;

        public ConfigurationRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret, ILogger<ConfigurationRepository> logger, IKeyValueStore keyValueStore )
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _logger = logger;
            _keyValueStore = keyValueStore;
        }

        private IMongoDatabase ResolveNotificationDb()
        {
            var blocksContext = BlocksContext.GetContext() ?? throw new InvalidOperationException("Tenant context is required.");
            if (blocksContext.Impersonated)
            {
                return _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName);
            }

            return _dbContextProvider.GetDatabase(blocksContext.TenantId);
        }

        public async Task<NotificationConfiguration> GetByNameAsync(string name)
        {
            // Send path (hot): KV first, then the old collection as today. No collection listing per send.
            // Impersonated reads root (KV impersonated:false), others the context tenant DB.
            var blocksContext = BlocksContext.GetContext() ?? throw new InvalidOperationException("Tenant context is required.");
            var fromKeyValueStore = await _keyValueStore.GetAsync<NotificationConfiguration>(
                NotificationKeyPrefix + (name ?? "").Trim(),
                impersonated: !blocksContext.Impersonated);
            if (fromKeyValueStore is not null)
            {
                return fromKeyValueStore;
            }

            var collection = ResolveNotificationDb().GetCollection<NotificationConfiguration>(_collectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.Name, name);
            var fromCollection = await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
            if (fromCollection is not null || blocksContext.Impersonated)
            {
                return fromCollection!;
            }

            return (await RootBuiltInAsync(name))!;
        }

        /// <summary>
        /// The root tenant's row, used only when it is a built-in: Console built-ins are kept in root only, but Blocks
        /// services still send them with a project's key (genesis reads them the same way). A root custom config never
        /// leaks out.
        /// </summary>
        private async Task<NotificationConfiguration?> RootBuiltInAsync(string? name)
        {
            var fromRoot = await _keyValueStore.GetAsync<NotificationConfiguration>(
                NotificationKeyPrefix + (name ?? "").Trim(),
                impersonated: false);
            return fromRoot is { IsBuiltIn: true } ? fromRoot : null;
        }

        public async Task<NotificationConfiguration> GetByIdAsync(string id)
        {
            var collection = ResolveNotificationDb().GetCollection<NotificationConfiguration>(_collectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, id);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task SaveAsync(NotificationConfiguration configuration)
        {
            var collection = ResolveNotificationDb().GetCollection<NotificationConfiguration>(_collectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, configuration.ItemId);

            await collection.ReplaceOneAsync(
                filter,
                configuration,
                new ReplaceOptions { IsUpsert = true }
            );
        }

        public async Task<GetConfigurationsResponse> GetConfigurationsAsync(GetConfigurationsRequest request)
        {
            var collection = ResolveNotificationDb().GetCollection<NotificationConfiguration>(_collectionName);
            var builder = Builders<NotificationConfiguration>.Filter;
            var filter = FilterDefinition<NotificationConfiguration>.Empty;
            var userId = BlocksContext.GetContext()?.UserId;

            var options = new FindOptions<NotificationConfiguration>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize,
                Sort = Builders<NotificationConfiguration>.Sort.Descending(n => n.CreatedDate)
            };

            var configurations = await (await collection.FindAsync(filter, options)).ToListAsync();
            var totalCount = await collection.CountDocumentsAsync(_ => true);

            return new GetConfigurationsResponse
            {
                Configurations = configurations,
                TotalCount = totalCount,
                IsSuccess = true
            };
        }

        public async Task<BaseResponse> DeleteConfigurationAsync(DeleteConfigurationRequest request)
        {
            var collection = ResolveNotificationDb().GetCollection<NotificationConfiguration>(_collectionName);
            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, request.ItemId);
            await collection.DeleteOneAsync(filter);
            
            return new BaseResponse { IsSuccess = true };
        }
    }
}
