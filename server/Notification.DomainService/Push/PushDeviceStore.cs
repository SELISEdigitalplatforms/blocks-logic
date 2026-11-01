using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;

namespace DomainService.Notification.Push
{
    /// <summary>One page of the admin device list. <see cref="SeenSince"/>: older devices are left out.</summary>
    public sealed record PushDeviceQuery(string? Platform, IReadOnlyCollection<string>? UserIds, DateTime SeenSince, int Skip, int Take);

    public sealed record PushDevicePage(IReadOnlyList<NotificationDevice> Items, long Total);

    /// <summary><c>NotificationDevices</c> reads and writes. Data access only.</summary>
    public interface IPushDeviceStore
    {
        /// <summary>Inserts the token, or moves it to <paramref name="device"/>'s user and refreshes it (a device that
        /// changed hands keeps one row). A null name keeps the stored one.</summary>
        Task UpsertAsync(PushDb db, NotificationDevice device, CancellationToken cancellationToken);

        /// <summary>Keeps the <paramref name="keep"/> devices of the user seen last; deletes the others.</summary>
        Task TrimAsync(PushDb db, string userId, int keep, CancellationToken cancellationToken);

        /// <summary>Deletes the device only when it belongs to <paramref name="userId"/>. False when nothing matched.</summary>
        Task<bool> DeleteOwnAsync(PushDb db, string userId, string deviceId, CancellationToken cancellationToken);

        /// <summary>Deletes one device by id (refused token, or an admin removing it). False when nothing matched.</summary>
        Task<bool> DeleteAsync(PushDb db, string deviceId, CancellationToken cancellationToken);

        /// <summary>The devices of these users seen at or after <paramref name="seenSince"/>.</summary>
        Task<IReadOnlyList<NotificationDevice>> GetForUsersAsync(PushDb db, IReadOnlyCollection<string> userIds, DateTime seenSince,
            CancellationToken cancellationToken);

        /// <summary>Devices per platform seen at or after <paramref name="seenSince"/>; only these users when given.</summary>
        Task<IReadOnlyDictionary<string, int>> CountByPlatformAsync(PushDb db, DateTime seenSince, IReadOnlyCollection<string>? userIds,
            CancellationToken cancellationToken);

        /// <summary>Devices newest first, filtered by <paramref name="query"/>, with the total of the filter.</summary>
        Task<PushDevicePage> FindPageAsync(PushDb db, PushDeviceQuery query, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushDeviceStore"/>
    /// <remarks>
    /// Each DB gets a TTL index on <c>LastSeenAt</c> (<see cref="NotificationDevice.MaxIdle"/>) once per process, before
    /// its first read or write. A failed index creation is tried again on the next call.
    /// </remarks>
    public sealed class PushDeviceStore : IPushDeviceStore
    {
        public const string TtlIndexName = "LastSeenAt_ttl";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;
        private readonly ConcurrentDictionary<string, Task> _ttlIndexes = new(StringComparer.Ordinal);

        public PushDeviceStore(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        private static FilterDefinitionBuilder<NotificationDevice> Filter => Builders<NotificationDevice>.Filter;

        public async Task UpsertAsync(PushDb db, NotificationDevice device, CancellationToken cancellationToken)
        {
            var update = Builders<NotificationDevice>.Update
                .Set(d => d.UserId, device.UserId)
                .Set(d => d.Token, device.Token)
                .Set(d => d.Platform, device.Platform)
                .Set(d => d.LastSeenAt, device.LastSeenAt)
                .SetOnInsert(d => d.CreatedAt, device.CreatedAt);
            if (device.Name is not null)
                update = update.Set(d => d.Name, device.Name);

            var devices = await DevicesAsync(db);
            await devices.UpdateOneAsync(Filter.Eq(d => d.Id, device.Id), update, new UpdateOptions { IsUpsert = true }, cancellationToken);
        }

        public async Task TrimAsync(PushDb db, string userId, int keep, CancellationToken cancellationToken)
        {
            var devices = await DevicesAsync(db);
            var extra = await devices
                .Find(Filter.Eq(d => d.UserId, userId))
                .SortByDescending(d => d.LastSeenAt)
                .Skip(keep)
                .Project(d => d.Id)
                .ToListAsync(cancellationToken);
            if (extra.Count > 0)
            {
                await devices.DeleteManyAsync(Filter.In(d => d.Id, extra), cancellationToken);
            }
        }

        public async Task<bool> DeleteOwnAsync(PushDb db, string userId, string deviceId, CancellationToken cancellationToken)
        {
            var devices = await DevicesAsync(db);
            var result = await devices.DeleteOneAsync(Filter.Eq(d => d.Id, deviceId) & Filter.Eq(d => d.UserId, userId), cancellationToken);
            return result.DeletedCount > 0;
        }

        public async Task<bool> DeleteAsync(PushDb db, string deviceId, CancellationToken cancellationToken)
        {
            var devices = await DevicesAsync(db);
            var result = await devices.DeleteOneAsync(Filter.Eq(d => d.Id, deviceId), cancellationToken);
            return result.DeletedCount > 0;
        }

        public async Task<IReadOnlyList<NotificationDevice>> GetForUsersAsync(PushDb db, IReadOnlyCollection<string> userIds, DateTime seenSince,
            CancellationToken cancellationToken)
        {
            if (userIds.Count == 0)
                return [];

            var devices = await DevicesAsync(db);
            return await devices.Find(Filter.In(d => d.UserId, userIds) & Filter.Gte(d => d.LastSeenAt, seenSince)).ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyDictionary<string, int>> CountByPlatformAsync(PushDb db, DateTime seenSince, IReadOnlyCollection<string>? userIds,
            CancellationToken cancellationToken)
        {
            var devices = await DevicesAsync(db);
            var groups = await devices.Aggregate()
                .Match(Matching(null, userIds, seenSince))
                .Group(d => d.Platform, g => new { Platform = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            return groups.ToDictionary(g => g.Platform ?? string.Empty, g => g.Count, StringComparer.Ordinal);
        }

        public async Task<PushDevicePage> FindPageAsync(PushDb db, PushDeviceQuery query, CancellationToken cancellationToken)
        {
            var devices = await DevicesAsync(db);
            var filter = Matching(query.Platform, query.UserIds, query.SeenSince);
            var total = await devices.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
            var items = await devices.Find(filter)
                .SortByDescending(d => d.LastSeenAt).ThenBy(d => d.Id)
                .Skip(query.Skip)
                .Limit(query.Take)
                .ToListAsync(cancellationToken);
            return new PushDevicePage(items, total);
        }

        private static FilterDefinition<NotificationDevice> Matching(string? platform, IReadOnlyCollection<string>? userIds, DateTime seenSince)
        {
            var filter = Filter.Gte(d => d.LastSeenAt, seenSince);
            if (platform is not null)
                filter &= Filter.Eq(d => d.Platform, platform);
            if (userIds is not null)
                filter &= Filter.In(d => d.UserId, userIds);
            return filter;
        }

        private async Task<IMongoCollection<NotificationDevice>> DevicesAsync(PushDb db)
        {
            var database = db.Root
                ? _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName)
                : _dbContextProvider.GetDatabase(db.TenantId);
            var collection = database.GetCollection<NotificationDevice>(NotificationDevice.CollectionName);

            var key = db.Root ? "root" : "tenant:" + db.TenantId;
            var ensure = _ttlIndexes.GetOrAdd(key, _ => CreateTtlIndexAsync(collection));
            try
            {
                await ensure;
            }
            catch (Exception)
            {
                // Forget the failed attempt so the next call tries again; the caller sees the error.
                _ttlIndexes.TryRemove(new KeyValuePair<string, Task>(key, ensure));
                throw;
            }

            return collection;
        }

        private static async Task CreateTtlIndexAsync(IMongoCollection<NotificationDevice> collection)
        {
            try
            {
                await collection.Indexes.CreateOneAsync(new CreateIndexModel<NotificationDevice>(
                    Builders<NotificationDevice>.IndexKeys.Ascending(d => d.LastSeenAt),
                    new CreateIndexOptions { ExpireAfter = NotificationDevice.MaxIdle, Name = TtlIndexName }));
            }
            catch (MongoCommandException ex) when (ex.CodeName is "IndexOptionsConflict" or "IndexKeySpecsConflict")
            {
                // An index on LastSeenAt with other options exists already. Sends still skip old devices by query.
            }
        }
    }
}
