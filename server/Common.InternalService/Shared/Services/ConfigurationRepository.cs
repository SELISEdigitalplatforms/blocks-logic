using Blocks.Genesis;
using MongoDB.Driver;
using Common.InternalService.Notification.Entities;
using Common.InternalService.Notification.ResponseModel;
using Common.InternalService.Notification.RequestModel;
using Common.InternalService.Storage.Entities;

namespace Common.InternalService.Shared.Services
{
    public class ConfigurationRepository : IConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        private const string _notificatonConfigurationCollectionName = "NotificationConfigurations";
        private const string _storageCollectionName = "StorageConfigurations";
        private readonly IBlocksSecret _blocksSecret;

        public ConfigurationRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        #region Notification

        public async Task<NotificationConfiguration> GetNotificationConfigurationByIdAsync(string id)
        {
            var collection = _dbContextProvider.GetCollection<NotificationConfiguration>(_notificatonConfigurationCollectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, id);
            return await(await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request)
        {
            var blocksContext = BlocksContext.GetContext();
            IMongoCollection<NotificationConfiguration> collection;

            if (blocksContext.Impersonated)
            {
                var database = _dbContextProvider.GetDatabase(
                    _blocksSecret.DatabaseConnectionString,
                    "BlocksRootDb");

                collection = database.GetCollection<NotificationConfiguration>(
                    _notificatonConfigurationCollectionName);
            }
            else
            {
                collection = _dbContextProvider.GetCollection<NotificationConfiguration>(
                    _notificatonConfigurationCollectionName);
            }

            var filter = FilterDefinition<NotificationConfiguration>.Empty;

            var options = new FindOptions<NotificationConfiguration>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize,
                Sort = Builders<NotificationConfiguration>.Sort.Descending(n => n.CreatedDate)
            };

            var configurations = await(await collection.FindAsync(filter, options)).ToListAsync();
            var totalCount = await collection.CountDocumentsAsync(_ => true);

            return new GetNotificationConfigurationsResponse
            {
                Configurations = configurations,
                TotalCount = totalCount,
                IsSuccess = true
            };
        }

        #endregion

        #region Storage

        public async Task<StorageConfiguration> GetStorageConfigurationByNameAsync(string configurationName)
        {
            var collection = _dbContextProvider.GetCollection<StorageConfiguration>(_storageCollectionName);

            var filter = Builders<StorageConfiguration>.Filter.Eq(mc => mc.Name, configurationName);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<StorageConfiguration>> GetAllStorageConfigurationsByDateAsync()
        {
            var collection = _dbContextProvider.GetCollection<StorageConfiguration>(_storageCollectionName);
            var filter = Builders<StorageConfiguration>.Filter.Where(_ => true);

            using var cursor = await collection.FindAsync(filter, new FindOptions<StorageConfiguration>
            {
                Sort = Builders<StorageConfiguration>.Sort.Ascending(doc => doc.LastUpdatedDate)
            });

            return await cursor.ToListAsync();
        }

        #endregion
    }
}
