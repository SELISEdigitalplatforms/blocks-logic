using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace DomainService.Notification.Push
{
    /// <summary>Makes sure one notification id is pushed once, even when the queue delivers it again.</summary>
    public interface IPushDedupStore
    {
        /// <summary>True for the first claim of <paramref name="notificationId"/> within <see cref="PushDedupStore.Window"/>.</summary>
        Task<bool> TryClaimAsync(Guid notificationId, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushDedupStore"/>
    /// <remarks>
    /// Insert first: the unique <c>_id</c> makes two workers racing on the same id agree (one insert wins). Rows expire
    /// after <see cref="Window"/> (TTL index) in the ROOT DB, so ids from every tenant share one small collection.
    /// </remarks>
    public sealed class PushDedupStore : IPushDedupStore
    {
        public const string CollectionName = "NotificationPushDedup";
        public static readonly TimeSpan Window = TimeSpan.FromHours(1);

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _indexLock = new(1, 1);
        private volatile bool _indexReady;

        public PushDedupStore(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret, TimeProvider timeProvider)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _timeProvider = timeProvider;
        }

        public async Task<bool> TryClaimAsync(Guid notificationId, CancellationToken cancellationToken)
        {
            var collection = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName)
                .GetCollection<PushClaim>(CollectionName);
            await EnsureIndexAsync(collection, cancellationToken);

            try
            {
                await collection.InsertOneAsync(new PushClaim
                {
                    Id = notificationId.ToString("N"),
                    CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                }, cancellationToken: cancellationToken);
                return true;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return false;
            }
        }

        private async Task EnsureIndexAsync(IMongoCollection<PushClaim> collection, CancellationToken cancellationToken)
        {
            if (_indexReady)
                return;

            await _indexLock.WaitAsync(cancellationToken);
            try
            {
                if (!_indexReady)
                {
                    await collection.Indexes.CreateOneAsync(new CreateIndexModel<PushClaim>(
                        Builders<PushClaim>.IndexKeys.Ascending(c => c.CreatedAt),
                        new CreateIndexOptions { ExpireAfter = Window, Name = "CreatedAt_ttl" }), cancellationToken: cancellationToken);
                    _indexReady = true;
                }
            }
            finally
            {
                _indexLock.Release();
            }
        }

        [BsonIgnoreExtraElements]
        private sealed class PushClaim
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }
    }
}
