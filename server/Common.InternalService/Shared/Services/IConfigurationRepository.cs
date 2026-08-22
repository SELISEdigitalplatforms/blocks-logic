using Common.InternalService.Notification.Entities;
using Common.InternalService.Notification.RequestModel;
using Common.InternalService.Notification.ResponseModel;
using Common.InternalService.Storage.Entities;

namespace Common.InternalService.Shared.Services
{
    public interface IConfigurationRepository
    {
        #region Notification

        Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request);
        Task<NotificationConfiguration> GetNotificationConfigurationByIdAsync(string id);

        #endregion

        #region Storage

        Task<StorageConfiguration> GetStorageConfigurationByNameAsync(string configurationName);
        Task<List<StorageConfiguration>> GetAllStorageConfigurationsByDateAsync();

        #endregion
    }
}
