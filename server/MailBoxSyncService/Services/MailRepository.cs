using Blocks.Genesis;
using DataAccess.InternalService;
using Mail.DomainService.Entities;
using MailBoxSyncService.Entities;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Logic;

namespace MailBoxSyncService.Services
{
    public class MailRepository : IMailRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        private readonly ILogicDataAccess _dataAccess;

        public MailRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _dataAccess = dataAccess;
        }

        public async Task<bool> ExistsAsync(string messageId, string tenantId)
        {
            var dbContext = _dbContextProvider.GetDatabase(tenantId);
            var mailCollection = dbContext.GetCollection<MailBoxEntity>($"{nameof(MailBoxEntity)}s");
            var filter = Builders<MailBoxEntity>.Filter.Eq(x => x.MessageId, messageId);
            // Sync worker: no user context, so the helper takes its no-user-context skip path.
            var scope = await _dataAccess.ReadAsync(LogicSchemas.MailBoxEntity, filter, "MailBoxSync.MailRepository.Exists", tenantId, useProjection: false);
            return scope.Granted && await mailCollection.Find(scope.Filter).AnyAsync();
        }

        public async Task InsertAsync(MailBoxEntity mail, string tenantId)
        {
            var dbContext = _dbContextProvider.GetDatabase(tenantId);
            var mailCollection = dbContext.GetCollection<MailBoxEntity>($"{nameof(MailBoxEntity)}s");
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.MailBoxEntity, "MailBoxSync.MailRepository.Insert", tenantId))
                throw new UnauthorizedAccessException("Mailbox mail create is not allowed by the data access policy.");
            await _dataAccess.PrepareWriteAsync(LogicSchemas.MailBoxEntity, mail, isInsert: true, "MailBoxSync.MailRepository.Insert", tenantId);
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
            var scope = await _dataAccess.ReadAsync(LogicSchemas.MailBoxSyncCursor, filter, "MailBoxSync.MailRepository.GetSyncCursor", tenantId);
            return scope.Granted ? await scope.Apply(collection).FirstOrDefaultAsync() : null;
        }

        public async Task SaveSyncCursorAsync(MailBoxSyncCursor cursor, string tenantId)
        {
            var collection = SyncCursors(tenantId);
            var filter = Builders<MailBoxSyncCursor>.Filter.Eq(x => x.ConfigurationId, cursor.ConfigurationId);
            // Upsert: the EDIT filter is ANDed on the match; a denied EDIT means no write (insert half not gated separately).
            var edit = await _dataAccess.EditFilterAsync(LogicSchemas.MailBoxSyncCursor, filter, "MailBoxSync.MailRepository.SaveSyncCursor", tenantId);
            if (edit is null) return;
            await collection.ReplaceOneAsync(edit, cursor, new ReplaceOptions { IsUpsert = true });
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
