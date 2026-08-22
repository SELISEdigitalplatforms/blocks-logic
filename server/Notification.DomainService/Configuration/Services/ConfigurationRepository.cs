using Blocks.Genesis;
using DomainService.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainService.Configuration.Services
{
    public class ConfigurationRepository : IConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "NotificationConfigurations";
        private readonly IBlocksSecret _blocksSecret;
        private readonly ILogger<ConfigurationRepository> _logger;

        public ConfigurationRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret, ILogger<ConfigurationRepository> logger )
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _logger = logger;
        }

        private IMongoDatabase ResolveNotificationDb()
        {
            var blocksContext = BlocksContext.GetContext();
            _logger.LogInformation($"Blocks Context {blocksContext.ToString()}");
            if (blocksContext.Impersonated)
            {
                _logger.LogInformation($"Blocks Impersonated {blocksContext.Impersonated}");
                _logger.LogInformation($"Database {_blocksSecret.DatabaseConnectionString}");
                return _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, "BlocksRootDb");
            }

            return _dbContextProvider.GetDatabase(blocksContext.TenantId);
        }

        public async Task<NotificationConfiguration> GetByNameAsync(string name)
        {
            var notificationDb = ResolveNotificationDb();
            var collection = notificationDb.GetCollection<NotificationConfiguration>(_collectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.Name, name);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }
    }
}
