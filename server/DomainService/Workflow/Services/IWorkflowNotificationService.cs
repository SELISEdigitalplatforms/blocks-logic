using DomainService.Workflow.Dtos;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Services
{
    public interface IWorkflowNotificationService
    {
        public Task<bool> Notify(List<string> userIds, NotificationData data);

        public Task NotifyExecutionEventAsync(
            WorkflowExecutionModel execution,
            NodeExecutionModel? nodeExecution,
            string eventName,
            string code,
            string status,
            string data,
            string message);
    }
}