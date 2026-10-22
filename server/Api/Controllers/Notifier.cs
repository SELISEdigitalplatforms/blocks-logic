using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis.Entities.Logic;

namespace Api.Controllers
{
    /// <summary>
    /// Delivery and read-state for user notifications, routed as <c>/api/Notifier/{action}</c>.
    /// The two send actions take no bearer token because internal services call them; they check the
    /// <c>Secret</c> sender header instead (<see cref="INotifierSendGuard"/>). Everything that reads or
    /// mutates a user's own notifications requires a bearer token.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]

    public class NotifierController : ControllerBase
    {
        private const string SecretHeader = "Secret";

        private readonly INotificationService _notificationService;
        private readonly INotifierSendGuard _sendGuard;
        private readonly ILogger<NotifierController> _logger;

        /// <summary>Takes the notification service, the send guard and a logger.</summary>
        public NotifierController(INotificationService notificationService, INotifierSendGuard sendGuard,
            ILogger<NotifierController> logger)
        {
            _notificationService = notificationService;
            _sendGuard = sendGuard;
            _logger = logger;
        }

        private bool SenderAllowed(string action) =>
            _sendGuard.IsAllowed(Request.Headers[SecretHeader].ToString(), action);

        // Shape only, never the payload: ResponseValue / DenormalizedPayload can carry a secret
        // (SendSecretNotification is the secrets pipeline). User rule 2026-10-08.
        private void LogReceived(NotifyRequest notifyRequest) =>
            _logger.LogInformation(
                "Received notification request: configuration {ConfigurationName}, {UserCount} user id(s), {RoleCount} role(s), {FilterCount} filter(s)",
                notifyRequest?.ConfigurationName, notifyRequest?.UserIds?.Count ?? 0, notifyRequest?.Roles?.Count ?? 0,
                notifyRequest?.SubscriptionFilters?.Count ?? 0);

        private void LogHandled(BaseResponse response) =>
            _logger.LogInformation("Notification handled: success {IsSuccess}, {ErrorCount} error(s)",
                response?.IsSuccess, response?.Errors?.Count ?? 0);

        /// <summary><c>POST</c> — delivers a notification through the configured channels.</summary>
        [HttpPost]
        public async Task<ActionResult<BaseResponse>> Notify([FromBody] NotifyRequest notifyRequest)
        {
            if (!SenderAllowed(nameof(Notify)))
                return Unauthorized();

            LogReceived(notifyRequest);
            var response= await _notificationService.NotifyAsync(notifyRequest);
            LogHandled(response);
            return response;
        }

        /// <summary>
        /// <c>POST</c> — the secrets-pipeline variant of <see cref="Notify"/>, hidden from the API explorer.
        /// Same service call; kept separate so its traffic is distinguishable in logs.
        /// </summary>
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost]
        public async Task<ActionResult<BaseResponse>> SendSecretNotification([FromBody] NotifyRequest notifyRequest)
        {
            if (!SenderAllowed(nameof(SendSecretNotification)))
                return Unauthorized();

            LogReceived(notifyRequest);

            var response = await _notificationService.NotifyAsync(notifyRequest);

            LogHandled(response);
            return response;
        }

        /// <summary>
        /// <c>GET</c> — the caller's unread notifications matching a subscription filter. Note the filter
        /// arrives in the body despite the <c>GET</c> verb.
        /// </summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::notifier::read")]
        public async Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter([FromBody] GetUnreadNotificationsRequestBySubscriptionFilter request)
        {
            return await _notificationService.GetUnreadNotificationsBySubscriptionFilter(request);
        }

        /// <summary><c>POST</c> — marks every one of the caller's notifications read.</summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::notifier::manage")]
        public async Task<BaseResponse> MarkAllNotificationAsRead()
        {
            return await _notificationService.MarkAllNotificationAsRead();
        }

        /// <summary><c>POST</c> — marks one notification read.</summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::notifier::manage")]
        public async Task<BaseResponse> MarkNotificationAsRead([FromBody] MarkNotificationAsReadRequest request)
        {
            return await _notificationService.MarkNotificationAsRead(request);
        }

        /// <summary><c>GET</c> — the caller's notifications, filtered and paged by the query string.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::notifier::read")]
        public async Task<GetNotificationsResponse> GetNotifications([FromQuery] GetNotificationsRequest request)
        {
            return await _notificationService.GetNotificationsAsync(request);
        }
    }
}
