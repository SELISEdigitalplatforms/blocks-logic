using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DomainService.Workflow.Services
{
    public class WorkflowNotificationService : IWorkflowNotificationService
    {
        private readonly ICryptoService _cryptoService;
        private readonly ITenants _tenants;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WorkflowNotificationService> _logger;
        private readonly INotificationService _notificationService;

        public WorkflowNotificationService(
            ICryptoService cryptoService,
            ITenants tenants,
            IConfiguration configuration,
            INotificationService notificationService,
            ILogger<WorkflowNotificationService> logger)
        {
            _cryptoService = cryptoService;
            _tenants = tenants;
            _configuration = configuration;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<bool> Notify(List<string> userIds, NotificationData data)
        {
            var notifyRequest = new NotifyRequest
            {
                ConnectionId = string.Empty,
                Roles = new List<string>(),
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
                ContentAvailable = true,
                ConfiguratoinName = _configuration["WORKFLOW_NOTIFICATION_CONFIGURATION_NAME"],
                ResponseKey = data.ResponseKey,
                ResponseValue = data.ResponseValue,
            };

            var response = await _notificationService.NotifyAsync(notifyRequest);

            if (response.IsSuccess)
            {
                _logger.LogInformation("Successfully sent notification to users : {Users}", string.Join(", ", userIds));
            }
            else
            {
                _logger.LogError("Failed to send notification to users : {Users}. Errors : {Errors}",
                    string.Join(", ", userIds),
                    response.Errors != null ? string.Join(", ", response.Errors.Select(kv => $"{kv.Key}={kv.Value}")) : "none");
            }

            return response.IsSuccess;
        }

        public async Task<bool> NotifyWorkflowExecutionEvent(List<string> userIds, WorkflowModel workflowModel, Dictionary<string, string> data)
        {
            return await Notify(userIds, new NotificationData
            {
                Title = "Workflow Execution",
                Description = data["Message"],
                ResponseKey = "WorkflowExecution",
                ResponseValue = "Sent",
                Information = new Dictionary<string, object>
                {
                    { "WorkflowId", workflowModel.ItemId },
                    { "WorkflowName", workflowModel.Name },
                    { "TenantId", workflowModel.TenantId },
                    { "Event", data["Event"] },
                    { "Status", data["Status"] },
                    { "Message", data["Message"] },
                    { "Data", data.ContainsKey("Data") ? data["Data"] : null }
                }
            });
        }
    }
}
