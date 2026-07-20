using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class NotifierController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotifierController> _logger;

        public NotifierController(INotificationService notificationService,ILogger<NotifierController>logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        [HttpPost]
        //[Authorize]
        //[ProtectedEndPoint]
        public async Task<BaseResponse> Notify([FromBody] NotifyRequest notifyRequest)
        {
            _logger.LogInformation("Received notification request: {@NotifyRequest}", notifyRequest);
            var response= await _notificationService.NotifyAsync(notifyRequest);
            _logger.LogInformation("Notification response: {@Response}", response);
            return response;
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        //[SecretEndPoint]
        [HttpPost]
       //[Authorize]
        public async Task<BaseResponse> SendSecretNotification([FromBody] NotifyRequest notifyRequest)
        {
            _logger.LogInformation("Received notification request: {@NotifyRequest}", notifyRequest);

            var response = await _notificationService.NotifyAsync(notifyRequest);

            _logger.LogInformation("Notification response: {@Response}", response);
            return response;
        }

        [HttpGet]
        [Authorize]
        //[ProtectedEndPoint]
        public async Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter([FromBody] GetUnreadNotificationsRequestBySubscriptionFilter request)
        {
            return await _notificationService.GetUnreadNotificationsBySubscriptionFilter(request);
        }

        [HttpPost]
        [Authorize]
        //[ProtectedEndPoint]
        public async Task<BaseResponse> MarkAllNotificationAsRead()
        {
            return await _notificationService.MarkAllNotificationAsRead();
        }

        [HttpPost]
        [Authorize]
        // [ProtectedEndPoint]
        public async Task<BaseResponse> MarkNotificationAsRead([FromBody] MarkNotificationAsReadRequest request)
        {
            return await _notificationService.MarkNotificationAsRead(request);
        }

        [HttpGet]
        [Authorize]
        // [ProtectedEndPoint]
        public async Task<GetNotificationsResponse> GetNotifications([FromQuery] GetNotificationsRequest request)
        {
            return await _notificationService.GetNotificationsAsync(request);
        }
    }
}
