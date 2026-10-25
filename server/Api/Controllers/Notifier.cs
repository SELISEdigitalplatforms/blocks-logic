using Blocks.Genesis;
using BlocksTemplate.Api.Security;
using DomainService.Notification;
using DomainService.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis.Entities.Logic;

namespace Api.Controllers
{
    /// <summary>
    /// Delivery and read-state for user notifications, routed as <c>/api/Notifier/{action}</c>.
    /// The two send actions stay open without a bearer token because internal services call them with the
    /// <c>Secret</c> sender header; a caller without it needs a valid token with
    /// <c>blocks-logic::notifier::send</c> (<see cref="INotifierSendGuard"/>, <see cref="INotifierSendPermission"/>).
    /// Everything that reads or mutates a user's own notifications requires a bearer token.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]

    public class NotifierController : ControllerBase
    {
        private const string SecretHeader = "Secret";

        private readonly INotificationService _notificationService;
        private readonly INotifierSendGuard _sendGuard;
        private readonly INotifierSendPermission _sendPermission;
        private readonly ILogger<NotifierController> _logger;

        /// <summary>Takes the notification service, the send guard, the send permission check and a logger.</summary>
        public NotifierController(INotificationService notificationService, INotifierSendGuard sendGuard,
            INotifierSendPermission sendPermission, ILogger<NotifierController> logger)
        {
            _notificationService = notificationService;
            _sendGuard = sendGuard;
            _sendPermission = sendPermission;
            _logger = logger;
        }

        private Task<bool> SenderAllowedAsync(string? secret, string action) =>
            _sendGuard.IsAllowedAsync(secret, () => _sendPermission.CheckAsync(HttpContext), action);

        // The Genesis permission handler can answer 429 itself (resource quota); then nothing more is written.
        private ActionResult Refused() =>
            Response.HasStarted ? new EmptyResult() : Unauthorized();

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
        [NotifierOptionalToken]
        public async Task<ActionResult<BaseResponse>> Notify([FromBody] NotifyRequest notifyRequest,
            [FromHeader(Name = SecretHeader)] string? secret = null)
        {
            if (!await SenderAllowedAsync(secret, nameof(Notify)))
                return Refused();

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
        [NotifierOptionalToken]
        public async Task<ActionResult<BaseResponse>> SendSecretNotification([FromBody] NotifyRequest notifyRequest,
            [FromHeader(Name = SecretHeader)] string? secret = null)
        {
            if (!await SenderAllowedAsync(secret, nameof(SendSecretNotification)))
                return Refused();

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
