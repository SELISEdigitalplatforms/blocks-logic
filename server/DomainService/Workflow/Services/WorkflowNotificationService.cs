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

        public async Task NotifyExecutionEventAsync(
            WorkflowExecutionModel execution,
            NodeExecutionModel? nodeExecution,
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
            WorkflowModel workflowModel,
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
            WorkflowModel workflowModel,
            NodeExecutionModel nodeExecution,
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