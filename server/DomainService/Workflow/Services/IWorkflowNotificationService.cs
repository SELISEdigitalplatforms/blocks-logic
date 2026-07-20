using DomainService.Workflow.Dtos;
using DomainService.Workflow.Entities;

namespace DomainService.Workflow.Services
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