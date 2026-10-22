using Blocks.Genesis.Entities.Os;

namespace DomainService.Notification
{
    /// <summary>When a notification configuration lets a notification go out.</summary>
    public static class NotificationConfigurationSendRules
    {
        /// <summary>
        /// True when the tenant switched this configuration off and it may be switched off. A built-in job result or
        /// progress stream is always sent, even if <c>IsDisabled</c> is set. Records without the built-in fields read
        /// as not disabled, so they are sent as before.
        /// </summary>
        public static bool IsDisabledByTenant(this NotificationConfiguration configuration) =>
            configuration is { IsDisabled: true } && configuration.CanBeDisabled();
    }
}
