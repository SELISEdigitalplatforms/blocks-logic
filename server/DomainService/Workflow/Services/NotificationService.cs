using Blocks.Genesis;
using DomainService.Workflow.Dtos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DomainService.Workflow.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ICryptoService _cryptoService;
        private readonly ITenants _tenants;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NotificationService> _logger;

        private readonly IHttpService _httpService;
        public NotificationService(ICryptoService cryptoService, ITenants tenants, IConfiguration configuration, IHttpService httpService, ILogger<NotificationService> logger)
        {
            _cryptoService = cryptoService;
            _tenants = tenants;
            _configuration = configuration;
            _httpService = httpService;
            _logger = logger;
        }

        public async Task<bool> Notify(List<string> userIds, NotificationData data)
        {
            var payload = new
            {
                ConnectionId = "",
                Roles = new List<string> { },
                UserIds = userIds,
                DenormalizedPayload = JsonSerializer.Serialize(new
                {
                    Message = new
                    {
                        title = data.Title,
                        description = data.Description
                    },
                    Information = data.Information
                }),
                SaveDenormalizedPayloadAsAnObject = false,
                ConfiguratoinName = _configuration["BlocksAppNotificationReceiver"],
                ContentAvailable = true,
                ResponseKey = data.ResponseKey,
                ResponseValue = data.ResponseValue,
            };
            var rootTenantId = _configuration["RootTenantId"]; ;
            var salt = _tenants.GetTenantByID(rootTenantId)?.TenantSalt;
            var actulalSecret = _cryptoService.Hash(rootTenantId, salt);


            var url = _configuration["NotificationServiceUrl"];
            var headers = new Dictionary<string, string>
            {
                { "x-blocks-key", rootTenantId },
                { "Secret", actulalSecret}
            };
            var contentType = "application/json";
            var (response, rawResponse) = await _httpService.Post<NotificationResponse>(payload, url, contentType, headers);
            if (response.isSuccess)
            {
                _logger.LogInformation($"Successfully sent notification to users : {string.Join(", ", userIds)}");
            }
            else
            {
                _logger.LogError($"Failed to sent notification to users : {string.Join(", ", userIds)}. Error :  {response.errors}");
            }
            return true;
        }

        // public async Task<bool> NotifyFeatureExecutionEvent(string appId, string tenantId, Dictionary<string, string> data)
        // {
        //     var loggedUser = BlocksContext.GetContext()?.UserId;
        //     var userIds = new List<string>
        //     {
        //         loggedUser
        //     };
        //     return await Notify(userIds, new NotificationData
        //     {
        //         Title = $"Feature Execution",
        //         Description = $"{data["Message"]}",
        //         ResponseKey = "FeatureExecution",
        //         ResponseValue = $"Sent",
        //         Information = new Dictionary<string, object>
        //         {
        //             { "AppId", appId },
        //             { "TenantId", tenantId },
        //             { "Event", "FeatureExecution" },
        //             { "Status", data["Status"] },
        //             { "Message", data["Message"] }
        //         }
        //     });

        // }

    }
}
