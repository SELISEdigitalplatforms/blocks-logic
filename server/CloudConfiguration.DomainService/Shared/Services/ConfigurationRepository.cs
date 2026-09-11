using Blocks.Genesis;
using MongoDB.Driver;
using System.Linq.Expressions;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public class ConfigurationRepository : IConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        private const string _captchaConfigurationCollectionName = "CaptchaConfigurations";
        private const string _iamConfigurationCollectionName = "IamConfigurations";
        private const string _notificatonConfigurationCollectionName = "NotificationConfigurations";
        private const string _storageCollectionName = "StorageConfigurations";
        private const string _mailConfigurationCollectionName = "MailServerConfigurations";
        private readonly IBlocksSecret _blocksSecret;

        public ConfigurationRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        #region Notification

        public async Task SaveNotificationConfigurationAsync(NotificationConfiguration configuration)
        {
            var collection = _dbContextProvider.GetCollection<NotificationConfiguration>(_notificatonConfigurationCollectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, configuration.ItemId);

            await collection.ReplaceOneAsync(
                filter,
                configuration,
                new ReplaceOptions { IsUpsert = true }
            );
        }

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
        
        public async Task<NotificationConfiguration> GetNotificationConfigurationByNameAsync(string name)
        {
            var collection = _dbContextProvider.GetCollection<NotificationConfiguration>(_notificatonConfigurationCollectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.Name, name);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<BaseResponse> DeleteNotificationConfigurationAsync(DeleteNotificatoinConfigurationRequest request)
        {
            var collection = _dbContextProvider.GetCollection<NotificationConfiguration>(_notificatonConfigurationCollectionName);
            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, request.ItemId);
            await collection.DeleteOneAsync(filter);

            return new BaseResponse { IsSuccess = true };
        }

        #endregion

        #region Storage

        public async Task SaveStorageConfigurationAsync(StorageConfiguration configuration)
        {
            var collection = _dbContextProvider.GetCollection<StorageConfiguration>(_storageCollectionName);

            var filter = Builders<StorageConfiguration>.Filter.Eq(mc => mc.ItemId, configuration.ItemId);

            await collection.ReplaceOneAsync(
                filter,
                configuration,
                new ReplaceOptions { IsUpsert = true }
            );
        }

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

        public async Task DeleteStorageConfigurationByNameAsync(string configurationName)
        {
            var collection = _dbContextProvider.GetCollection<StorageConfiguration>(_storageCollectionName);
            var filter = Builders<StorageConfiguration>.Filter.Eq(mc => mc.Name, configurationName);

            await collection.DeleteOneAsync(filter);
        }

        public async Task<StorageConfiguration> GetStorageConfigurationByIdAsync(string itemId)
        {
            var collection = _dbContextProvider.GetCollection<StorageConfiguration>(_storageCollectionName);

            var filter = Builders<StorageConfiguration>.Filter.Eq(mc => mc.ItemId, itemId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<StorageConfiguration?> GetStorageConfigurationStrategyAsync(string storageStrategy)
        {
            var collection = _dbContextProvider.GetCollection<StorageConfiguration>(_storageCollectionName);

            var filter = Builders<StorageConfiguration>.Filter.Eq(mc => mc.StorageStrategy, storageStrategy);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        #endregion

        #region Mail

        public async Task SaveMailConfigurationAsync(MailServerConfiguration configuration)
        {
            var collection = _dbContextProvider.GetCollection<MailServerConfiguration>(_mailConfigurationCollectionName);

            var filter = Builders<MailServerConfiguration>.Filter.Eq(mc => mc.ItemId, configuration.ItemId);

            await collection.ReplaceOneAsync(filter, configuration, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<MailServerConfiguration> GetMailConfigurationByIdAsync(string configurationId)
        {
            var collection = _dbContextProvider.GetCollection<MailServerConfiguration>(_mailConfigurationCollectionName);
            var filter = Builders<MailServerConfiguration>.Filter.Eq(mc => mc.ItemId, configurationId);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<MailConfiguration> GetMailConfigurationByNameAsync(string configurationName)
        {
            var collection = _dbContextProvider.GetCollection<MailConfiguration>(_mailConfigurationCollectionName);
            var filter = Builders<MailConfiguration>.Filter.Eq(mc => mc.ConfigurationName, configurationName);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<MailServerConfiguration>> GetAllMailConfigurationsAsync()
        {
            var collection = _dbContextProvider.GetCollection<MailServerConfiguration>(_mailConfigurationCollectionName);
            var document = collection.Find(_ => true).SortByDescending(doc => doc.IsDefault);

            return await document.ToListAsync();
        }

        public async Task DeleteMailConfigurationAsync(string configurationId)
        {
            var collection = _dbContextProvider.GetCollection<MailServerConfiguration>(_mailConfigurationCollectionName);
            var filter = Builders<MailServerConfiguration>.Filter.Eq(mc => mc.ItemId, configurationId);

            await collection.DeleteOneAsync(filter);
        }

        #endregion

        public async Task UpsertAsync<T>(T data, Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);

            var options = new ReplaceOptions { IsUpsert = true };
            await collection.ReplaceOneAsync(filterExpression, data, options);
        }   
    }
}
