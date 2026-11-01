using Blocks.Genesis;
using DomainService.Shared;
using Blocks.Genesis.Entities.Logic;

namespace DomainService.Notification
{
    /// <summary>Hub connections and subscriptions, and the caller's inbox.</summary>
    public interface INotificationService
    {
        Task CreateConnectionAsync(string connectionId);
        Task RemoveCollectionAsync(string collectionId);
        Task<BaseResponse> AddSubscriptionAsync(Subscription request);
        Task<BaseResponse> RemoveSubscriptionAsync(Subscription subscription);
        Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter(GetUnreadNotificationsRequestBySubscriptionFilter request);
        Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request);
        Task<BaseResponse> MarkAllNotificationAsRead();
        Task<BaseResponse> MarkNotificationAsRead(MarkNotificationAsReadRequest request);
    }
}
