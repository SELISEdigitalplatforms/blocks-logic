using Blocks.Genesis;
using Functions.DomainService.Events;
using Functions.DomainService.Utils;
using MongoDB.Driver;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionEventSettingsRepository"/>
    /// <remarks>
    /// Logic-owned collection in the tenant's database, reached through <see cref="IDbContextProvider"/> like the other
    /// Functions repositories. It has no Genesis schema, so no data-access policy applies to it: the access check is the
    /// endpoint's permission and the function lookup in the tenant's own database.
    /// </remarks>
    public class FunctionEventSettingsRepository : IFunctionEventSettingsRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionEventSettingsRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoCollection<FunctionEventSettingsEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionEventSettingsEntity>(tenantId, FunctionsConstants.FunctionEventSettingsCollection);

        private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
        {
            lock (_indexGate)
            {
                if (!_indexedTenants.Add(tenantId)) return;
            }

            // Purge reads by function; the row itself is found by its id.
            await Collection(tenantId).Indexes.CreateOneAsync(
                new CreateIndexModel<FunctionEventSettingsEntity>(
                    Builders<FunctionEventSettingsEntity>.IndexKeys.Ascending(s => s.FunctionId)),
                cancellationToken: cancellationToken);
        }

        public async Task<FunctionEventSettingsEntity?> GetAsync(
            string tenantId, string functionId, string? versionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return null;

            var id = FunctionEventSettingsEntity.IdFor(functionId, string.IsNullOrEmpty(versionId) ? null : versionId);
            return await Collection(tenantId)
                .Find(s => s.ItemId == id && s.FunctionId == functionId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task SaveDraftAsync(
            string tenantId, string functionId, List<FunctionEventSetting> events, List<string> triggerEvents,
            string? actorId, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);

            var now = DateTime.UtcNow;
            var id = FunctionEventSettingsEntity.IdFor(functionId, null);
            var update = Builders<FunctionEventSettingsEntity>.Update
                .Set(s => s.FunctionId, functionId)
                .Set(s => s.VersionId, null)
                .Set(s => s.Events, events)
                .Set(s => s.TriggerEvents, triggerEvents)
                .Set(s => s.LastUpdatedDate, now)
                .Set(s => s.LastUpdatedBy, actorId)
                .SetOnInsert(s => s.CreatedDate, now);

            await UpsertAsync(tenantId, id, update, cancellationToken);
        }

        public async Task CopyDraftToVersionAsync(
            string tenantId, string functionId, string versionId, CancellationToken cancellationToken = default)
        {
            var draft = await GetAsync(tenantId, functionId, null, cancellationToken);
            if (draft is null) return;

            await EnsureIndexesAsync(tenantId, cancellationToken);

            var now = DateTime.UtcNow;
            var update = Builders<FunctionEventSettingsEntity>.Update
                .Set(s => s.FunctionId, functionId)
                .Set(s => s.VersionId, versionId)
                .Set(s => s.Events, draft.Events)
                .Set(s => s.TriggerEvents, draft.TriggerEvents)
                .Set(s => s.LastUpdatedDate, now)
                .Set(s => s.LastUpdatedBy, draft.LastUpdatedBy)
                .SetOnInsert(s => s.CreatedDate, now);

            await UpsertAsync(tenantId, FunctionEventSettingsEntity.IdFor(functionId, versionId), update, cancellationToken);
        }

        public async Task<long> DeleteVersionsAsync(
            string tenantId, IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken = default)
        {
            if (versionIds.Count == 0) return 0;

            var filter = Builders<FunctionEventSettingsEntity>.Filter.In(s => s.VersionId, versionIds);
            var result = await Collection(tenantId).DeleteManyAsync(filter, cancellationToken);
            return result.DeletedCount;
        }

        public async Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return 0;

            var result = await Collection(tenantId).DeleteManyAsync(s => s.FunctionId == functionId, cancellationToken);
            return result.DeletedCount;
        }

        /// <summary>Upsert by id. Two first writes at once can collide on the id; the loser simply updates.</summary>
        private async Task UpsertAsync(
            string tenantId, string id, UpdateDefinition<FunctionEventSettingsEntity> update, CancellationToken cancellationToken)
        {
            var options = new UpdateOptions { IsUpsert = true };
            try
            {
                await Collection(tenantId).UpdateOneAsync(s => s.ItemId == id, update, options, cancellationToken);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                await Collection(tenantId).UpdateOneAsync(s => s.ItemId == id, update, options, cancellationToken);
            }
        }
    }
}
