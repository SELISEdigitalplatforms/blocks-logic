using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;

namespace DomainService.Notification.Push
{
    /// <summary>
    /// Reads the <c>firebase-config</c> KV record of the tenant in <see cref="BlocksContext"/>. Read only: blocks-os owns
    /// the push configuration and is its only writer. Data access only.
    /// </summary>
    public interface IPushSettingsStore
    {
        Task<PushSettingsRecord?> GetAsync(CancellationToken cancellationToken);
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
    }
}
