using WebPush;

namespace DomainService.Notification
{
    /// <summary>Thin seam over <see cref="WebPushClient"/> so unit tests can record sends without the network.</summary>
    public interface IWebPushSender
    {
        Task SendAsync(PushSubscription subscription, string payload, VapidDetails vapidDetails, CancellationToken cancellationToken = default);
    }

    public sealed class WebPushSender : IWebPushSender
    {
        private readonly WebPushClient _client = new();

        public Task SendAsync(PushSubscription subscription, string payload, VapidDetails vapidDetails, CancellationToken cancellationToken = default) =>
            _client.SendNotificationAsync(subscription, payload, vapidDetails);
    }
}
