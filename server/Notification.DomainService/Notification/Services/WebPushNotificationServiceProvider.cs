using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Shared;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification
{
    /// <summary>
    /// Queues one <see cref="WebPushDeliveryCommand"/> per registered device and returns. The
    /// Worker's delivery consumer does the encrypted send, retry, and 410 cleanup.
    /// </summary>
    public class WebPushNotificationServiceProvider : INotifier
    {
        private readonly ILogger<WebPushNotificationServiceProvider> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly IMessageClient _messageClient;

        public WebPushNotificationServiceProvider(
            ILogger<WebPushNotificationServiceProvider> logger,
            INotificationRepository notificationRepository,
            IMessageClient messageClient)
        {
            _logger = logger;
            _notificationRepository = notificationRepository;
            _messageClient = messageClient;
        }

        public async Task Notify(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(notifyRequest);

            var payload = notifyRequest.DenormalizedPayload ?? string.Empty;
            var userIds = notifyRequest.UserIds ?? [];
            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            var queued = 0;

            foreach (var userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                var subscriptions = await _notificationRepository
                    .GetItemsAsync<WebPushSubscription>(s => s.UserId == userId)
                    .ConfigureAwait(false);

                foreach (var subscription in subscriptions)
                {
                    await PublishAsync(new WebPushDeliveryCommand
                    {
                        SubscriptionEndpoint = subscription.Endpoint,
                        KeysP256dh = subscription.Keys?.P256dh ?? string.Empty,
                        KeysAuth = subscription.Keys?.Auth ?? string.Empty,
                        UserId = userId,
                        TenantId = tenantId,
                        DenormalizedPayload = payload,
                        Attempt = 1,
                    }).ConfigureAwait(false);
                    queued++;
                }
            }

            _logger.LogInformation("WebPush: queued {Count} delivery command(s)", queued);
        }

        private async Task PublishAsync(WebPushDeliveryCommand command)
        {
            try
            {
                await _messageClient.SendToConsumerAsync(new ConsumerMessage<WebPushDeliveryCommand>
                {
                    ConsumerName = WebPushConstants.DeliveryQueueName,
                    Payload = command,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebPush: could not queue delivery for user {UserId}", command.UserId);
                throw new WebPushQueueUnavailableException(WebPushConstants.QueueErrorMessage, ex);
            }
        }
    }
}
