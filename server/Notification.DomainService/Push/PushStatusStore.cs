using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Notification.Push
{
    /// <summary>The last push test of one platform. <see cref="Reason"/>: the first failure code, null when all went.</summary>
    [BsonIgnoreExtraElements]
    public sealed class PushLastTest
    {
        public DateTime At { get; set; }
        public int Delivered { get; set; }
        public int Failed { get; set; }

        [BsonIgnoreIfNull]
        public string? Reason { get; set; }
    }

    /// <summary>KV <c>firebase-push-status</c> (tenant DB): logic's own record of the last test per platform.</summary>
    [BsonIgnoreExtraElements]
    public sealed class PushStatusRecord
    {
        public const string KeyValueKey = "firebase-push-status";

        public PushLastTest? Android { get; set; }
        public PushLastTest? Ios { get; set; }
        public PushLastTest? Web { get; set; }

        public PushLastTest? Get(string platform) => platform switch
        {
            PushPlatforms.Android => Android,
            PushPlatforms.Ios => Ios,
            PushPlatforms.Web => Web,
            _ => null,
        };

        public void Set(string platform, PushLastTest test)
        {
            switch (platform)
            {
                case PushPlatforms.Android:
                    Android = test;
                    break;
                case PushPlatforms.Ios:
                    Ios = test;
                    break;
                case PushPlatforms.Web:
                    Web = test;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(platform));
            }
        }
    }

    /// <summary>Reads and writes <see cref="PushStatusRecord"/> of the context tenant. Data access only.</summary>
    public interface IPushStatusStore
    {
        Task<PushStatusRecord?> GetAsync(CancellationToken cancellationToken);
        Task SaveAsync(PushStatusRecord record, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushStatusStore"/>
    public sealed class PushStatusStore : IPushStatusStore
    {
        private readonly IKeyValueStore _keyValueStore;

        public PushStatusStore(IKeyValueStore keyValueStore)
        {
            _keyValueStore = keyValueStore;
        }

        // impersonated: true = the context tenant's DB (the project), also for an impersonated root user.
        public Task<PushStatusRecord?> GetAsync(CancellationToken cancellationToken) =>
            _keyValueStore.GetAsync<PushStatusRecord>(PushStatusRecord.KeyValueKey, impersonated: true, cancellationToken);

        public Task SaveAsync(PushStatusRecord record, CancellationToken cancellationToken) =>
            _keyValueStore.SetAsync(PushStatusRecord.KeyValueKey, record, impersonated: true, cancellationToken);
    }
}
