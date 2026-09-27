using Blocks.Genesis;
using Mail.DomainService.Entities;
using MailBoxSyncService.Entities;
using MongoDB.Driver;

namespace MailBoxSyncService.Services
{
    public class MailRepository : IMailRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        public MailRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        public async Task<bool> ExistsAsync(string messageId, string tenantId)
        {
            var dbContext = _dbContextProvider.GetDatabase(tenantId);
            var mailCollection = dbContext.GetCollection<MailBoxEntity>($"{nameof(MailBoxEntity)}s");
            var filter = Builders<MailBoxEntity>.Filter.Eq(x => x.MessageId, messageId);
            return await mailCollection.Find(filter).AnyAsync();
        }

        public async Task InsertAsync(MailBoxEntity mail, string tenantId)
        {
            var dbContext = _dbContextProvider.GetDatabase(tenantId);
            var mailCollection = dbContext.GetCollection<MailBoxEntity>($"{nameof(MailBoxEntity)}s");
            await mailCollection.InsertOneAsync(mail);
        }

        public async Task<List<Tenant>> GetTenantsAsync()
        {
            var dbContext = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName);
            var tenantCollection = dbContext.GetCollection<Tenant>("Tenants");
            var filter = Builders<Tenant>.Filter.Eq(x => x.IsDisabled, false);
            return await (await tenantCollection.FindAsync(filter)).ToListAsync();
        }

        public async Task<List<MailServerConfiguration>> GetImapConfigurationsAsync(Tenant tenant)
        {
            var dbContext = _dbContextProvider.GetDatabase(tenant.TenantId);
            var configCollection = dbContext.GetCollection<MailServerConfiguration>("MailServerConfigurations");
            var filter = Builders<MailServerConfiguration>.Filter.Where(c => c.IsInbound);
            return await (await configCollection.FindAsync(filter)).ToListAsync();
        }

        public async Task<MailBoxSyncCursor?> GetSyncCursorAsync(string configurationId, string tenantId)
        {
            var collection = SyncCursors(tenantId);
            var filter = Builders<MailBoxSyncCursor>.Filter.Eq(x => x.ConfigurationId, configurationId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task SaveSyncCursorAsync(MailBoxSyncCursor cursor, string tenantId)
        {
            var collection = SyncCursors(tenantId);
            var filter = Builders<MailBoxSyncCursor>.Filter.Eq(x => x.ConfigurationId, cursor.ConfigurationId);
            await collection.ReplaceOneAsync(filter, cursor, new ReplaceOptions { IsUpsert = true });
        }

        private IMongoCollection<MailBoxSyncCursor> SyncCursors(string tenantId) =>
            _dbContextProvider.GetDatabase(tenantId).GetCollection<MailBoxSyncCursor>($"{nameof(MailBoxSyncCursor)}s");
    }

    public interface IMailRepository
    {
        Task<bool> ExistsAsync(string messageId, string tenantId);
        Task InsertAsync(MailBoxEntity mail, string tenantId);
        Task<List<Tenant>> GetTenantsAsync();
        Task<List<MailServerConfiguration>> GetImapConfigurationsAsync(Tenant tenant);
        Task<MailBoxSyncCursor?> GetSyncCursorAsync(string configurationId, string tenantId);
        Task SaveSyncCursorAsync(MailBoxSyncCursor cursor, string tenantId);
    }
}
