using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;

namespace DomainService.Notification.Push
{
    /// <summary>The <c>firebase-config</c> KV record of the tenant in <see cref="BlocksContext"/>. Data access only.</summary>
    public interface IPushSettingsStore
    {
        Task<PushSettingsRecord?> GetAsync(CancellationToken cancellationToken);
        Task SaveAsync(PushSettingsRecord record, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushSettingsStore"/>
    public sealed class PushSettingsStore : IPushSettingsStore
    {
        private readonly IKeyValueStore _keyValueStore;

        public PushSettingsStore(IKeyValueStore keyValueStore)
        {
            _keyValueStore = keyValueStore;
        }

        // impersonated: true = the context tenant's DB (the project), also for an impersonated root user.
        public Task<PushSettingsRecord?> GetAsync(CancellationToken cancellationToken) =>
            _keyValueStore.GetAsync<PushSettingsRecord>(FirebasePushConfiguration.KeyValueKey, impersonated: true, cancellationToken);

        public Task SaveAsync(PushSettingsRecord record, CancellationToken cancellationToken) =>
            _keyValueStore.SetAsync(FirebasePushConfiguration.KeyValueKey, record, impersonated: true, cancellationToken);
    }
}
