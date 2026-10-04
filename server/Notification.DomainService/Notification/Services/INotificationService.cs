using Blocks.Genesis;
using DomainService.Shared;

namespace DomainService.Notification
{
    public interface INotificationService
    {
        Task CreateConnectionAsync(string connectionId);
        Task RemoveCollectionAsync(string collectionId);
        Task<BaseResponse> AddSubscriptionAsync(Subscription request);
        Task<BaseResponse> RemoveSubscriptionAsync(Subscription subscription);
        Task<BaseResponse> NotifyAsync(NotifyRequest notifyRequest);
        Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter(GetUnreadNotificationsRequestBySubscriptionFilter request);
        Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request);
        Task<BaseResponse> MarkAllNotificationAsRead();
        Task<BaseResponse> MarkNotificationAsRead(MarkNotificationAsReadRequest request);
        Task<BaseResponse> RegisterWebPushSubscriptionAsync(RegisterWebPushSubscriptionRequest request);
        Task<BaseResponse> UnregisterWebPushSubscriptionAsync(UnregisterWebPushSubscriptionRequest request);
        Task<GetWebPushPublicKeyResponse> GetWebPushPublicKeyAsync();
        Task<BaseResponse> RotateWebPushVapidKeysAsync();
    }
}
