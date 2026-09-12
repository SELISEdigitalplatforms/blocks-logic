using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>Notification-channel configuration, routed as <c>/api/Notification/{action}</c>.</summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class NotificationController
    {
        private readonly IConfigurationService _configurationService;


        /// <summary>Takes the configuration service.</summary>
        public NotificationController ( IConfigurationService configurationService
                                       )
            {
            _configurationService = configurationService;

            }



        /// <summary><c>GET</c> — the tenant's notification configurations, filtered by the query string.</summary>
        [HttpGet]
        [Authorize]
        public async Task<GetNotificationConfigurationsResponse> Gets ( [FromQuery] GetNotificationConfigurationsRequest request )
        {
        return await _configurationService.GetNotificationConfigurationsAsync(request);
        }

        /// <summary><c>GET</c> — one notification configuration.</summary>
        [HttpGet]
        [Authorize]
        public async Task<NotificationConfiguration> Get ( [FromQuery] GetNotificationConfigurationRequest request )
        {
        return await _configurationService.GetNotificatoinConfigurationAsync(request);
        }

    }
}
