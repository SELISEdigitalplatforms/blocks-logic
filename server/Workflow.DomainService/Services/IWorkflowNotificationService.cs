using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Services
{
    public interface IWorkflowNotificationService
    {
        public Task<bool> Notify(List<string> userIds, NotificationData data);

        public Task<bool> NotifyImportAsync(
            List<string> userIds,
            string correlationId,
            bool isSuccess,
            string title,
            string description,
            string? workflowId,
            int issues);

        public Task NotifyExecutionEventAsync(
            WorkflowExecutionEntity execution,
            NodeExecutionEntity? nodeExecution,
            string eventName,
            string code,
            string status,
            string data,
            string message);

        /// <summary>A schedule trigger fired but no run started. Tells the workflow owner. Never throws.</summary>
        public Task NotifyScheduledTriggerDroppedAsync(string tenantId, WorkflowEntity workflow, string reason);
    }
}