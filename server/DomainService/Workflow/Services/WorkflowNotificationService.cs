using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DomainService.Workflow.Services
{
    public class WorkflowNotificationService : IWorkflowNotificationService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ICryptoService _cryptoService;
        private readonly ITenants _tenants;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WorkflowNotificationService> _logger;
        private readonly INotificationService _notificationService;
        private readonly IHttpClientFactory _httpClientFactory;

        public WorkflowNotificationService(
            ICryptoService cryptoService,
            ITenants tenants,
            IConfiguration configuration,
            INotificationService notificationService,
            IHttpClientFactory httpClientFactory,
            ILogger<WorkflowNotificationService> logger)
        {
            _cryptoService = cryptoService;
            _tenants = tenants;
            _configuration = configuration;
            _notificationService = notificationService;
            _httpClientFactory = httpClientFactory;
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
                ConfiguratoinName = _configuration["WORKFLOW_NOTIFICATION_CONFIGURATION_NAME"],
                ContentAvailable = true,
                ResponseKey = data.ResponseKey,
                ResponseValue = data.ResponseValue,
            };
            var blocksKey = _configuration["RootTenantId"];
            var salt = _tenants.GetTenantByID(blocksKey)?.TenantSalt;
            var actulalSecret = _cryptoService.Hash(blocksKey, salt);


            var url = _configuration["NotificationServiceUrl"];
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogError("NotificationServiceUrl is not configured; cannot send notifications to users: {UserIds}.", string.Join(", ", userIds));
                return false;
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-blocks-key", blocksKey);
                request.Headers.Add("Secret", actulalSecret);

                using var httpResponse = await httpClient.SendAsync(request);
                var rawResponse = await httpResponse.Content.ReadAsStringAsync();

                NotificationResponse? response = null;
                if (!string.IsNullOrWhiteSpace(rawResponse))
                {
                    try
                    {
                        response = JsonSerializer.Deserialize<NotificationResponse>(rawResponse, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize notification response. Raw response: {RawResponse}", rawResponse);
                    }
                }

                var isSuccess = httpResponse.IsSuccessStatusCode && (response?.isSuccess ?? false);
                if (isSuccess)
                {
                    _logger.LogInformation("Successfully sent notification to users : {UserIds}", string.Join(", ", userIds));
                    return true;
                }

                var errorMessage = response?.errors
                    ?? $"HTTP {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}";
                _logger.LogError(
                    "Failed to sent notification to users : {UserIds}. Error : {Error}",
                    string.Join(", ", userIds),
                    errorMessage);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sent notification to users : {UserIds}", string.Join(", ", userIds));
                return false;
            }
        }

        public async Task NotifyExecutionEventAsync(
            WorkflowExecutionEntity execution,
            NodeExecutionEntity? nodeExecution,
            string eventName,
            string code,
            string status,
            string data,
            string message)
        {
            var workflowSnapshot = execution.WorkflowSnapshot;
            if (workflowSnapshot == null) return;

            var userIds = workflowSnapshot.TestMeta?.UserIds;
            if (userIds == null || userIds.Count == 0) return;

            try
            {
                var executionId = execution.Id ?? string.Empty;
                if (nodeExecution != null)
                {
                    await NotifyNodeExecutionEventAsync(userIds, workflowSnapshot, nodeExecution, executionId, eventName, code, status, data, message);
                }
                else
                {
                    await NotifyWorkflowExecutionEventAsync(userIds, workflowSnapshot, executionId, eventName, code, status, data, message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send {Event} notification for execution {ExecutionId}.",
                    eventName, execution.Id);
            }
        }

        private async Task<bool> NotifyWorkflowExecutionEventAsync(
            List<string> userIds,
            WorkflowEntity workflowModel,
            string executionId,
            string eventName,
            string code,
            string status,
            string data,
            string message)
        {
            return await Notify(userIds, new NotificationData
            {
                Title = "Workflow Execution",
                Description = message,
                ResponseKey = "WorkflowExecution",
                ResponseValue = "Sent",
                Information = new Dictionary<string, object>
                {
                    { "workflowId", workflowModel.ItemId },
                    { "workflowName", workflowModel.Name },
                    { "tenantId", workflowModel.TenantId },
                    { "executionId", executionId },
                    { "event", eventName },
                    { "code", code },
                    { "status", status },
                    { "data", data },
                    { "message", message }
                }
            });
        }

        private async Task<bool> NotifyNodeExecutionEventAsync(
            List<string> userIds,
            WorkflowEntity workflowModel,
            NodeExecutionEntity nodeExecution,
            string executionId,
            string eventName,
            string code,
            string status,
            string data,
            string message)
        {
            return await Notify(userIds, new NotificationData
            {
                Title = "Node Execution",
                Description = message,
                ResponseKey = "NodeExecution",
                ResponseValue = "Sent",
                Information = new Dictionary<string, object>
                {
                    { "workflowId", workflowModel.ItemId },
                    { "workflowName", workflowModel.Name },
                    { "tenantId", workflowModel.TenantId },
                    { "executionId", executionId },
                    { "nodeId", nodeExecution.NodeId },
                    { "nodeName", nodeExecution.NodeName },
                    { "nodeType", nodeExecution.NodeType },
                    { "nodeExecutionId", nodeExecution.Id },
                    { "event", eventName },
                    { "code", code },
                    { "status", status },
                    { "data", data },
                    { "message", message }
                }
            });
        }
    }
}