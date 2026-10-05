using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// Delivery and read-state for user notifications, routed as <c>/api/Notifier/{action}</c>.
    /// The two send actions are anonymous because internal services call them; everything that reads or
    /// mutates a user's own notifications requires a bearer token.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]

    public class NotifierController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotifierController> _logger;

        /// <summary>Takes the notification service and a logger.</summary>
        public NotifierController(INotificationService notificationService,ILogger<NotifierController>logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        /// <summary><c>POST</c> — delivers a notification through the configured channels.</summary>
        [HttpPost]
        public async Task<BaseResponse> Notify([FromBody] NotifyRequest notifyRequest)
        {
            _logger.LogInformation("Received notification request: {@NotifyRequest}", notifyRequest);
            var response= await _notificationService.NotifyAsync(notifyRequest);
            _logger.LogInformation("Notification response: {@Response}", response);
            return response;
        }

        /// <summary>
        /// <c>POST</c> — the secrets-pipeline variant of <see cref="Notify"/>, hidden from the API explorer.
        /// Same service call; kept separate so its traffic is distinguishable in logs.
        /// </summary>
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost]
        public async Task<BaseResponse> SendSecretNotification([FromBody] NotifyRequest notifyRequest)
        {
            _logger.LogInformation("Received notification request: {@NotifyRequest}", notifyRequest);

            var response = await _notificationService.NotifyAsync(notifyRequest);

            _logger.LogInformation("Notification response: {@Response}", response);
            return response;
        }

        /// <summary>
        /// <c>GET</c> — the caller's unread notifications matching a subscription filter. Note the filter
        /// arrives in the body despite the <c>GET</c> verb.
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter([FromBody] GetUnreadNotificationsRequestBySubscriptionFilter request)
        {
            return await _notificationService.GetUnreadNotificationsBySubscriptionFilter(request);
        }

        /// <summary><c>POST</c> — marks every one of the caller's notifications read.</summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> MarkAllNotificationAsRead()
        {
            return await _notificationService.MarkAllNotificationAsRead();
        }

        /// <summary><c>POST</c> — marks one notification read.</summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> MarkNotificationAsRead([FromBody] MarkNotificationAsReadRequest request)
        {
            return await _notificationService.MarkNotificationAsRead(request);
        }

        /// <summary><c>GET</c> — the caller's notifications, filtered and paged by the query string.</summary>
        [HttpGet]
        [Authorize]
        public async Task<GetNotificationsResponse> GetNotifications([FromQuery] GetNotificationsRequest request)
        {
            return await _notificationService.GetNotificationsAsync(request);
        }

        /// <summary><c>POST</c> — registers (upserts) a browser Web Push subscription for the caller.</summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> RegisterWebPushSubscription([FromBody] RegisterWebPushSubscriptionRequest request)
        {
            return await _notificationService.RegisterWebPushSubscriptionAsync(request);
        }

        /// <summary><c>POST</c> — removes a browser Web Push subscription for the caller (no-op if missing).</summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> UnregisterWebPushSubscription([FromBody] UnregisterWebPushSubscriptionRequest request)
        {
            return await _notificationService.UnregisterWebPushSubscriptionAsync(request);
        }

        /// <summary><c>GET</c> — returns the tenant VAPID public key, creating a keypair on first call.</summary>
        [HttpGet]
        [Authorize]
        public async Task<GetWebPushPublicKeyResponse> GetWebPushPublicKey()
        {
            return await _notificationService.GetWebPushPublicKeyAsync();
        }

        /// <summary><c>POST</c> — rotates the tenant VAPID keypair and clears all Web Push subscriptions.</summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> RotateWebPushVapidKeys()
        {
            return await _notificationService.RotateWebPushVapidKeysAsync();
        }
    }
}
