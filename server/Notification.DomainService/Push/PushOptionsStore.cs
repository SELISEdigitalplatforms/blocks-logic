using Blocks.Genesis;

namespace DomainService.Notification.Push
{
    /// <summary>Reads KV <c>notification-push-options:{ConfigName}</c> of the context tenant. Read only. Data access only.</summary>
    public interface IPushOptionsStore
    {
        /// <summary>The push options of <paramref name="configName"/>; null when the notification has none.</summary>
        Task<PushNotificationOptions?> GetAsync(string configName, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushOptionsStore"/>
    public sealed class PushOptionsStore : IPushOptionsStore
    {
        private readonly IKeyValueStore _keyValueStore;

        public PushOptionsStore(IKeyValueStore keyValueStore)
        {
            _keyValueStore = keyValueStore;
        }

        // impersonated: true = the context tenant's DB (the project).
        public Task<PushNotificationOptions?> GetAsync(string configName, CancellationToken cancellationToken) =>
            _keyValueStore.GetAsync<PushNotificationOptions>(PushNotificationOptions.KeyPrefix + configName, impersonated: true, cancellationToken);
    }
}
