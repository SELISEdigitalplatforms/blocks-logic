using DomainService.Entities;
using DomainService.Shared;
using Microsoft.Extensions.Logging;
using WebPush;

namespace DomainService.Notification
{
    public class WebPushNotificationServiceProvider : INotifier
    {
        private readonly ILogger<WebPushNotificationServiceProvider> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly IWebPushVapidKeyService _vapidKeyService;
        private readonly IWebPushSender _sender;

        public WebPushNotificationServiceProvider(
            ILogger<WebPushNotificationServiceProvider> logger,
            INotificationRepository notificationRepository,
            IWebPushVapidKeyService vapidKeyService,
            IWebPushSender? sender = null)
        {
            _logger = logger;
            _notificationRepository = notificationRepository;
            _vapidKeyService = vapidKeyService;
            _sender = sender ?? new WebPushSender();
        }

        public async Task Notify(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            var payload = notifyRequest.DenormalizedPayload ?? string.Empty;
            var userIds = notifyRequest.UserIds ?? [];
            if (userIds.Count == 0)
            {
                _logger.LogDebug("WebPush: no UserIds — nothing to deliver");
                return;
            }

            var (publicKey, privateKey, subject) = await _vapidKeyService.GetVapidDetailsAsync().ConfigureAwait(false);
            var vapid = new VapidDetails(subject, publicKey, privateKey);

            foreach (var userId in userIds)
            {
                if (string.IsNullOrWhiteSpace(userId))
                    continue;

                var subscriptions = await _notificationRepository
                    .GetItemsAsync<WebPushSubscription>(s => s.UserId == userId)
                    .ConfigureAwait(false);

                if (subscriptions.Count == 0)
                {
                    _logger.LogDebug("WebPush: user {UserId} has no registered devices", userId);
                    continue;
                }

                foreach (var subscription in subscriptions)
                {
                    try
                    {
                        var pushSubscription = new PushSubscription(
                            subscription.Endpoint,
                            subscription.Keys?.P256dh ?? string.Empty,
                            subscription.Keys?.Auth ?? string.Empty);

                        await _sender.SendAsync(pushSubscription, payload, vapid).ConfigureAwait(false);
                        _logger.LogInformation("WebPush: delivered to user {UserId} endpoint {Endpoint}", userId, subscription.Endpoint);
                    }
                    catch (Exception ex)
                    {
                        // Phase 1: continue remaining devices; retry/410 cleanup is Phase 2.
                        _logger.LogError(ex, "WebPush: delivery failed for user {UserId} endpoint {Endpoint}", userId, subscription.Endpoint);
                    }
                }
            }
        }
    }
}
