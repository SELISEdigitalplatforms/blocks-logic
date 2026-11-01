using Blocks.Genesis.Entities.Logic;
using DomainService.Shared;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Notification
{
    public interface INotificationRepository
    {
        Task SaveAsync<T>(T data, string collectionName = "");
        Task SaveAsync<T>(List<T> listOfData);
        Task<T> GetItemAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "");
        Task<List<T>> GetItemsAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "");
        Task DeleteAsync<T>(Expression<Func<T, bool>> dataFilters);
        Task MarkAllAsReadAsync(InboxViewer viewer);
        Task MarkAsReadAsync(InboxViewer viewer, string notificationId);
        Task<List<OfflineNotification>> GetInboxItemsAsync(FilterDefinition<OfflineNotification> filter);
        Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request, InboxViewer viewer);
    }
}
