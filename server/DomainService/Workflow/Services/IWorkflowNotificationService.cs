using DomainService.Workflow.Dtos;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Services
{
    public interface IWorkflowNotificationService
    {
        public Task<bool> Notify(List<string> userIds, NotificationData data);

        public Task<bool> NotifyWorkflowExecutionEvent(List<string> userIds, WorkflowModel workflowModel, Dictionary<string, string> data);

    }
}
