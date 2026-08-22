using Common.InternalService.Notification.Entities;
using Common.InternalService.Notification.RequestModel;
using Common.InternalService.Notification.ResponseModel;
using Common.InternalService.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class NotificationController
    {
        private readonly IConfigurationService _configurationService;


        public NotificationController ( IConfigurationService configurationService
                                       )
            {
            _configurationService = configurationService;

            }



        [HttpGet]
        [Authorize]
        public async Task<GetNotificationConfigurationsResponse> Gets ( [FromQuery] GetNotificationConfigurationsRequest request )
        {
        return await _configurationService.GetNotificationConfigurationsAsync(request);
        }

        [HttpGet]
        [Authorize]
        public async Task<NotificationConfiguration> Get ( [FromQuery] GetNotificationConfigurationRequest request )
        {
        return await _configurationService.GetNotificatoinConfigurationAsync(request);
        }

    }
}
