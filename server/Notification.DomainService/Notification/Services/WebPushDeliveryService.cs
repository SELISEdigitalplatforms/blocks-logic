using System.Net;
using Blocks.Genesis;
using DomainService.Shared;
using Microsoft.Extensions.Logging;
using WebPush;

namespace DomainService.Notification
{
    /// <summary>
    /// Sends one <see cref="WebPushDeliveryCommand"/> with the fixed policy: up to
    /// <see cref="WebPushConstants.MaxAttempts"/> attempts, waiting 5s before attempt 2 and 30s
    /// before attempt 3. HTTP 410 on any attempt deletes the device subscription and stops.
    /// </summary>
    /// <remarks>
    /// Runs in the Worker, which has no authenticated caller. The VAPID secret read and the
    /// tenant database lookup both need an authenticated, tenant-scoped <see cref="BlocksContext"/>,
    /// so one is built from the command's tenant for the duration of the delivery and the previous
    /// context is restored afterwards (same approach as Office365TokenProvider).
    /// </remarks>
    public class WebPushDeliveryService : IWebPushDeliveryService
    {
        private readonly ILogger<WebPushDeliveryService> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly IWebPushVapidKeyService _vapidKeyService;
        private readonly IWebPushSender _sender;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public WebPushDeliveryService(
            ILogger<WebPushDeliveryService> logger,
            INotificationRepository notificationRepository,
            IWebPushVapidKeyService vapidKeyService,
            IWebPushSender sender,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _logger = logger;
            _notificationRepository = notificationRepository;
            _vapidKeyService = vapidKeyService;
            _sender = sender;
            _delay = delay ?? Task.Delay;
        }

        private enum AttemptOutcome
        {
            Delivered,
            Gone,
            Retryable,
        }

        public async Task DeliverAsync(WebPushDeliveryCommand command, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (string.IsNullOrWhiteSpace(command.SubscriptionEndpoint) || string.IsNullOrWhiteSpace(command.TenantId))
            {
                _logger.LogWarning("WebPush: delivery command for user {UserId} has no endpoint or tenant; dropped", command.UserId);
                return;
            }

            var restore = BlocksContext.GetContext();
            try
            {
                EnterTenantContext(command, restore);
                await DeliverWithRetryAsync(command, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                BlocksContext.SetContext(restore, restore is not null);
            }
        }

        private async Task DeliverWithRetryAsync(WebPushDeliveryCommand command, CancellationToken cancellationToken)
        {
            var subscription = new PushSubscription(command.SubscriptionEndpoint, command.KeysP256dh, command.KeysAuth);
            var firstAttempt = Math.Clamp(command.Attempt, 1, WebPushConstants.MaxAttempts);
            VapidDetails? vapid = null;

            for (var attempt = firstAttempt; attempt <= WebPushConstants.MaxAttempts; attempt++)
            {
                if (attempt > firstAttempt)
                {
                    await _delay(WebPushConstants.RetryDelays[attempt - 2], cancellationToken).ConfigureAwait(false);
                }

                vapid ??= await TryLoadVapidAsync(command, attempt, cancellationToken).ConfigureAwait(false);
                var outcome = vapid is null
                    ? AttemptOutcome.Retryable
                    : await TrySendAsync(subscription, command, vapid, attempt, cancellationToken).ConfigureAwait(false);

                switch (outcome)
                {
                    case AttemptOutcome.Delivered:
                        if (attempt > 1)
                            _logger.LogInformation("WebPush: delivered to user {UserId}, recovered on retry (attempt {Attempt})", command.UserId, attempt);
                        else
                            _logger.LogInformation("WebPush: delivered to user {UserId} on attempt 1", command.UserId);
                        return;

                    case AttemptOutcome.Gone:
                        await _notificationRepository.DeleteAsync<WebPushSubscription>(
                            s => s.UserId == command.UserId && s.Endpoint == command.SubscriptionEndpoint).ConfigureAwait(false);
                        _logger.LogInformation("WebPush: endpoint gone (410) for user {UserId} on attempt {Attempt}; subscription deleted", command.UserId, attempt);
                        return;
                }
            }

            _logger.LogError("WebPush: delivery to user {UserId} failed after {MaxAttempts} attempts; giving up", command.UserId, WebPushConstants.MaxAttempts);
        }

        private async Task<VapidDetails?> TryLoadVapidAsync(WebPushDeliveryCommand command, int attempt, CancellationToken cancellationToken)
        {
            try
            {
                var (publicKey, privateKey, subject) = await _vapidKeyService.GetVapidDetailsAsync(cancellationToken).ConfigureAwait(false);
                return new VapidDetails(subject, publicKey, privateKey);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "WebPush: could not load VAPID keys for user {UserId} on attempt {Attempt}", command.UserId, attempt);
                return null;
            }
        }

        private async Task<AttemptOutcome> TrySendAsync(
            PushSubscription subscription,
            WebPushDeliveryCommand command,
            VapidDetails vapid,
            int attempt,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(WebPushConstants.AttemptTimeout);

            try
            {
                await _sender.SendAsync(subscription, command.DenormalizedPayload, vapid, timeout.Token).ConfigureAwait(false);
                return AttemptOutcome.Delivered;
            }
            catch (WebPushException ex) when (ex.StatusCode == HttpStatusCode.Gone)
            {
                return AttemptOutcome.Gone;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "WebPush: attempt {Attempt} failed for user {UserId}", attempt, command.UserId);
                return AttemptOutcome.Retryable;
            }
        }

        /// <summary>
        /// Authenticated context for the command's tenant. Blocks Secrets rejects any context that is
        /// not authenticated or has a blank tenant, and the repository routes by tenant id.
        /// </summary>
        private static void EnterTenantContext(WebPushDeliveryCommand command, BlocksContext? current)
        {
            BlocksContext.SetContext(
                BlocksContext.Create(
                    tenantId: command.TenantId,
                    roles: [],
                    userId: command.UserId,
                    isAuthenticated: true,
                    requestUri: string.Empty,
                    organizationId: current?.OrganizationId ?? string.Empty,
                    expireOn: DateTime.MinValue,
                    email: string.Empty,
                    permissions: [],
                    userName: string.Empty,
                    phoneNumber: string.Empty,
                    displayName: string.Empty,
                    oauthToken: string.Empty,
                    originalTenantId: command.TenantId,
                    applicationDomain: string.Empty),
                true);
        }
    }
}
