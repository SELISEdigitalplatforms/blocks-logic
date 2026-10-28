using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Notifications;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Logic;
using DataAccess.InternalService.Notifications;

namespace Workflow.DomainService.Services
{
    public class WorkflowNotificationService : IWorkflowNotificationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<WorkflowNotificationService> _logger;
        private readonly INotificationPublisher _publisher;
        private readonly IBuiltInNotificationSender? _builtIn;

        public WorkflowNotificationService(
            IConfiguration configuration,
            INotificationPublisher publisher,
            ILogger<WorkflowNotificationService> logger,
            IBuiltInNotificationSender? builtInNotifications = null)
        {
            _builtIn = builtInNotifications;
            _configuration = configuration;
            _publisher = publisher;
            _logger = logger;
        }


        public Task<bool> Notify(List<string> userIds, NotificationData data)
        {
            var denormalizedPayload = JsonSerializer.Serialize(new
            {
                Message = new
                {
                    title = data.Title,
                    description = data.Description
                },
                Information = data.Information
            });

            return SendNotifyAsync(
                userIds,
                denormalizedPayload,
                _configuration["WORKFLOW_NOTIFICATION_CONFIGURATION_NAME"],
                data.ResponseKey,
                data.ResponseValue);
        }

        public Task<bool> NotifyImportAsync(
            List<string> userIds,
            string correlationId,
            bool isSuccess,
            string title,
            string description,
            string? workflowId,
            int issues)
        {
            var denormalizedPayload = JsonSerializer.Serialize(new
            {
                Message = new
                {
                    IsSuccess = isSuccess,
                    title,
                    description,
                    workflowId,
                    issues
                }
            });

            // The same configuration as execution events: it exists wherever workflows run, and the console
            // already listens to its event (`WorkflowNotification`). A `workflow-import` configuration was
            // never created anywhere, so the import's result never reached the page (2026-10-08). The
            // page matches the result by correlationId; execution handlers ignore it (no Information).
            var configurationName = _configuration["WORKFLOW_NOTIFICATION_CONFIGURATION_NAME"];
            return SendNotifyAsync(
                userIds,
                denormalizedPayload,
                string.IsNullOrWhiteSpace(configurationName) ? LogicConstants.WorkflowImportNotificationConfigurationName : configurationName,
                correlationId,
                isSuccess.ToString());
        }

        /// <summary>
        /// Publishes in the ROOT tenant, as the HTTP send with the root key did before: the users are the console users
        /// who run the test, and their connections join the root tenant's user group (also while they work in a
        /// project). Returns true when queued; delivery is the publisher's job and is not awaited.
        /// </summary>
        private Task<bool> SendNotifyAsync(
            List<string> userIds,
            string denormalizedPayload,
            string? configurationName,
            string responseKey,
            string responseValue)
        {
            if (string.IsNullOrWhiteSpace(configurationName))
            {
                _logger.LogError("WORKFLOW_NOTIFICATION_CONFIGURATION_NAME is not configured; workflow notification not sent.");
                return Task.FromResult(false);
            }

            _publisher.Publish(new NotificationRequest
            {
                Name = configurationName,
                TenantId = _configuration["RootTenantId"],
                NamedUserIds = userIds,
                Payload = denormalizedPayload,
                ResponseKey = responseKey,
                ResponseValue = responseValue,
            });
            return Task.FromResult(true);
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

            if (eventName == WorkflowFailedEventName && execution.ExecutionMode == WorkflowExecutionMode.Production)
            {
                try
                {
                    await NotifyRunFailedAsync(execution, workflowSnapshot);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not send the run-failed notification for execution {ExecutionId} ({ExceptionType}).", execution.Id, ex.GetType().Name);
                }
            }

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

        private const string WorkflowFailedEventName = "WorkflowFailed";

        /// <summary>
        /// A production or scheduled run failed: tell the workflow owner (built-in <c>logic-workflow-run-failed</c>).
        /// Test runs have their own live stream and are not sent here. Best effort: the sender never throws.
        /// The owner is the only recipient; no project-members lookup exists yet.
        /// </summary>
        private async Task NotifyRunFailedAsync(WorkflowExecutionEntity execution, WorkflowEntity workflow)
        {
            if (_builtIn is null) return;
            var owner = workflow.CreatedBy;
            await _builtIn.SendAsync(
                execution.TenantId,
                BuiltInNotificationNames.WorkflowRunFailed,
                string.IsNullOrWhiteSpace(owner) ? null : [owner],
                new BuiltInNotificationPayload(
                    "WorkflowRunFailed",
                    "Workflow run failed",
                    "A workflow run failed. Open the run to see the failing step.",
                    IsSuccess: false,
                    Ids: new Dictionary<string, string?>
                    {
                        ["workflowId"] = workflow.ItemId,
                        ["executionId"] = execution.Id,
                    }),
                throttleKey: workflow.ItemId);
        }

        /// <summary>
        /// A schedule trigger fired but no run was started (workflow not published, snapshot or trigger node
        /// missing): tell the workflow owner (built-in <c>logic-scheduled-trigger-dropped</c>).
        /// </summary>
        public async Task NotifyScheduledTriggerDroppedAsync(string tenantId, WorkflowEntity workflow, string reason)
        {
            if (_builtIn is null) return;
            try
            {
                await SendTriggerDroppedAsync(tenantId, workflow, reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not send the trigger-dropped notification for workflow {WorkflowId} ({ExceptionType}).", workflow.ItemId, ex.GetType().Name);
            }
        }

        private async Task SendTriggerDroppedAsync(string tenantId, WorkflowEntity workflow, string reason)
        {
            var owner = workflow.CreatedBy;
            await _builtIn!.SendAsync(
                tenantId,
                BuiltInNotificationNames.ScheduledTriggerDropped,
                string.IsNullOrWhiteSpace(owner) ? null : [owner],
                new BuiltInNotificationPayload(
                    "ScheduledTriggerDropped",
                    "Scheduled trigger dropped",
                    "A scheduled trigger fired but no run started, so it was dropped.",
                    IsSuccess: false,
                    Ids: new Dictionary<string, string?>
                    {
                        ["workflowId"] = workflow.ItemId,
                        ["reason"] = reason,
                    }),
                throttleKey: workflow.ItemId);
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