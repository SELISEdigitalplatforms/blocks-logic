using Blocks.Genesis;
using Microsoft.AspNetCore.Mvc;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public interface IConfigurationService
    {
        #region Notification

        Task<BaseResponse> SaveNotificationConfigurationAsync(SaveNotificatonConfigurationRequest configuration);
        Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request);
        Task<NotificationConfiguration> GetNotificatoinConfigurationAsync(GetNotificationConfigurationRequest request);
        Task<BaseResponse> DeleteNotificationConfigurationAsync(DeleteNotificatoinConfigurationRequest request);

        #endregion

        #region Storage

        Task<BaseMutationResponse> SaveStorageConfigurationAsync(SaveStorageConfigurationRequest request);
        Task<List<StorageConfiguration>> GetStorageConfigurationsAsync();
        Task<StorageConfiguration> GetStorageConfigurationAsync(string configurationName);
        Task<BaseResponse> DeleteStorageConfigurationAsync(string configurationName);

        #endregion

        #region Mail

        Task<BaseMutationResponse> SaveMailConfigurationAsync(MailConfiguration configuration);
        Task<MailConfiguration> GetMailConfigurationAsync(GetMailConfigurationRequest request);
        Task<List<MailServerConfiguration>> GetAllMailConfigurationsAsync();
        Task<BaseMutationResponse> DeleteMailConfigurationAsync(DeleteMailConfigurationRequest request);
        Task<BaseMutationResponse> DuplicateMailConfigurationAsync(DuplicateMailConfigurationRequest request);

        #endregion
    }
}
