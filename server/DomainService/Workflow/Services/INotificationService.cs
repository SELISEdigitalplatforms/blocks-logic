using DomainService.Workflow.Dtos;

namespace DomainService.Workflow.Services
{
    public interface INotificationService
    {
        public Task<bool> Notify(List<string> userIds, NotificationData data);

    }
}
