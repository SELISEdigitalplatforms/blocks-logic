using Blocks.Genesis;
using MongoDB.Driver;

namespace DomainService.Notification.Push
{
    /// <summary><c>NotificationDevices</c> reads and writes. Data access only.</summary>
    public interface IPushDeviceStore
    {
        /// <summary>Inserts the token, or moves it to <paramref name="device"/>'s user and refreshes it (a device that
        /// changed hands keeps one row).</summary>
        Task UpsertAsync(PushDb db, NotificationDevice device, CancellationToken cancellationToken);

        /// <summary>Keeps the <paramref name="keep"/> devices of the user seen last; deletes the others.</summary>
        Task TrimAsync(PushDb db, string userId, int keep, CancellationToken cancellationToken);

        /// <summary>Deletes the device only when it belongs to <paramref name="userId"/>. False when nothing matched.</summary>
        Task<bool> DeleteOwnAsync(PushDb db, string userId, string deviceId, CancellationToken cancellationToken);

        /// <summary>Deletes a device whose token Firebase refused.</summary>
        Task DeleteAsync(PushDb db, string deviceId, CancellationToken cancellationToken);

        Task<IReadOnlyList<NotificationDevice>> GetForUsersAsync(PushDb db, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushDeviceStore"/>
    public sealed class PushDeviceStore : IPushDeviceStore
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        public PushDeviceStore(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        private static FilterDefinitionBuilder<NotificationDevice> Filter => Builders<NotificationDevice>.Filter;

        public Task UpsertAsync(PushDb db, NotificationDevice device, CancellationToken cancellationToken)
        {
            var update = Builders<NotificationDevice>.Update
                .Set(d => d.UserId, device.UserId)
                .Set(d => d.Token, device.Token)
                .Set(d => d.Platform, device.Platform)
                .Set(d => d.LastSeenAt, device.LastSeenAt)
                .SetOnInsert(d => d.CreatedAt, device.CreatedAt);
            return Devices(db).UpdateOneAsync(Filter.Eq(d => d.Id, device.Id), update, new UpdateOptions { IsUpsert = true }, cancellationToken);
        }

        public async Task TrimAsync(PushDb db, string userId, int keep, CancellationToken cancellationToken)
        {
            var extra = await Devices(db)
                .Find(Filter.Eq(d => d.UserId, userId))
                .SortByDescending(d => d.LastSeenAt)
                .Skip(keep)
                .Project(d => d.Id)
                .ToListAsync(cancellationToken);
            if (extra.Count > 0)
            {
                await Devices(db).DeleteManyAsync(Filter.In(d => d.Id, extra), cancellationToken);
            }
        }

        public async Task<bool> DeleteOwnAsync(PushDb db, string userId, string deviceId, CancellationToken cancellationToken)
        {
            var result = await Devices(db).DeleteOneAsync(Filter.Eq(d => d.Id, deviceId) & Filter.Eq(d => d.UserId, userId), cancellationToken);
            return result.DeletedCount > 0;
        }

        public Task DeleteAsync(PushDb db, string deviceId, CancellationToken cancellationToken) =>
            Devices(db).DeleteOneAsync(Filter.Eq(d => d.Id, deviceId), cancellationToken);

        public async Task<IReadOnlyList<NotificationDevice>> GetForUsersAsync(PushDb db, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
        {
            if (userIds.Count == 0)
                return [];

            return await Devices(db).Find(Filter.In(d => d.UserId, userIds)).ToListAsync(cancellationToken);
        }

        private IMongoCollection<NotificationDevice> Devices(PushDb db)
        {
            var database = db.Root
                ? _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName)
                : _dbContextProvider.GetDatabase(db.TenantId);
            return database.GetCollection<NotificationDevice>(NotificationDevice.CollectionName);
        }
    }
}
