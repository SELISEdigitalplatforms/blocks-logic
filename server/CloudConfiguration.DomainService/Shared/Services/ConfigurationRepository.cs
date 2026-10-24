using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Linq.Expressions;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Mail.RequestModel;
using Blocks.Genesis.Entities.Data;
using Blocks.Genesis.Entities.Os;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public class ConfigurationRepository : IConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        private const string _captchaConfigurationCollectionName = "CaptchaConfigurations";
        private const string _iamConfigurationCollectionName = "IamConfigurations";
        private const string _notificatonConfigurationCollectionName = "NotificationConfigurations";
        private const string _mailConfigurationCollectionName = "MailServerConfigurations";
        private const string NotificationKeyPrefix = "notification-config:";
        private readonly IBlocksSecret _blocksSecret;
        private readonly IKeyValueStore _keyValueStore;

        public ConfigurationRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret, IKeyValueStore keyValueStore)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _keyValueStore = keyValueStore;
        }

        // Live check: does the DB this method uses today still have the old collection? One extra
        // round trip, admin paths only. A null database (no context) keeps today's path.
        private static async Task<bool> LegacyNotificationCollectionExistsAsync(IMongoDatabase? database, string collectionName)
        {
            if (database is null)
            {
                return true;
            }

            using var names = await database.ListCollectionNamesAsync(new ListCollectionNamesOptions
            {
                Filter = new BsonDocument("name", collectionName)
            });

            return await names.AnyAsync();
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

        // Get (one) and Gets (list) must read the same DB, or a listed row opens as null:
        // impersonated → root DB (KV impersonated:false), others → the caller's tenant DB (KV impersonated:true).
        private (IMongoDatabase? Database, bool KvImpersonated) NotificationReadScope()
        {
            if (BlocksContext.GetContext()?.Impersonated == true)
            {
                return (_dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, "BlocksRootDb"), false);
            }

            return (_dbContextProvider.GetDatabase(), true);
        }

        public async Task<NotificationConfiguration> GetNotificationConfigurationByIdAsync(string id)
        {
            var (database, kvImpersonated) = NotificationReadScope();
            if (!await LegacyNotificationCollectionExistsAsync(database, _notificatonConfigurationCollectionName))
            {
                var all = await _keyValueStore.GetByPrefixAsync<NotificationConfiguration>(NotificationKeyPrefix, impersonated: kvImpersonated);
                return all.FirstOrDefault(c => c.ItemId == id)!;
            }

            var collection = database is not null
                ? database.GetCollection<NotificationConfiguration>(_notificatonConfigurationCollectionName)
                : _dbContextProvider.GetCollection<NotificationConfiguration>(_notificatonConfigurationCollectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, id);
            return await(await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request)
        {
            var (listDatabase, kvImpersonated) = NotificationReadScope();
            var collection = listDatabase is not null
                ? listDatabase.GetCollection<NotificationConfiguration>(_notificatonConfigurationCollectionName)
                : _dbContextProvider.GetCollection<NotificationConfiguration>(_notificatonConfigurationCollectionName);

            if (!await LegacyNotificationCollectionExistsAsync(listDatabase, _notificatonConfigurationCollectionName))
            {
                // Old collection is gone (moved to KeyValueStores): same sort, page and count, in memory.
                var all = await _keyValueStore.GetByPrefixAsync<NotificationConfiguration>(NotificationKeyPrefix, impersonated: kvImpersonated);
                return new GetNotificationConfigurationsResponse
                {
                    // PageSize <= 0 means "all rows", as Mongo Limit = 0 does today.
                    Configurations = (request.PageSize > 0
                        ? all.OrderByDescending(n => n.CreatedDate).Skip(request.PageSize * request.Page).Take(request.PageSize)
                        : all.OrderByDescending(n => n.CreatedDate)).ToList(),
                    TotalCount = all.Count,
                    IsSuccess = true
                };
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
