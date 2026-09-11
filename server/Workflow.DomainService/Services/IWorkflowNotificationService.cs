using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;

namespace Workflow.DomainService.Services
{
    public interface IWorkflowNotificationService
    {
        public Task<bool> Notify(List<string> userIds, NotificationData data);

        public Task NotifyExecutionEventAsync(
            WorkflowExecutionEntity execution,
            NodeExecutionEntity? nodeExecution,
            string eventName,
            string code,
            string status,
            string data,
            string message);
    }
}