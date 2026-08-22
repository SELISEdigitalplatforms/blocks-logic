using Common.InternalService.Notification.Entities;
using Common.InternalService.Notification.RequestModel;
using Common.InternalService.Notification.ResponseModel;
using Common.InternalService.Storage.Entities;

namespace Common.InternalService.Shared.Services
{
    public interface IConfigurationService
    {
        #region Notification

        Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request);
        Task<NotificationConfiguration> GetNotificatoinConfigurationAsync(GetNotificationConfigurationRequest request);

        #endregion

        #region Storage

        Task<List<StorageConfiguration>> GetStorageConfigurationsAsync();
        Task<StorageConfiguration> GetStorageConfigurationAsync(string configurationName);

        #endregion
    }
}
