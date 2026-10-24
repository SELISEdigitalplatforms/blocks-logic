using Blocks.Genesis;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Mail.RequestModel;
using System.Linq.Expressions;
using Blocks.Genesis.Entities.Data;
using Blocks.Genesis.Entities.Os;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public interface IConfigurationRepository
    {
        #region Notification

        Task SaveNotificationConfigurationAsync(NotificationConfiguration configuration);
        Task<NotificationConfiguration> GetNotificationConfigurationByIdAsync(string id);
        Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request);
        Task<BaseResponse> DeleteNotificationConfigurationAsync(DeleteNotificatoinConfigurationRequest request);
        Task<NotificationConfiguration> GetNotificationConfigurationByNameAsync(string name);

        #endregion

        #region Mail

        Task SaveMailConfigurationAsync(MailServerConfiguration configuration);
        Task<MailServerConfiguration> GetMailConfigurationByIdAsync(string configurationId);
        Task<MailConfiguration> GetMailConfigurationByNameAsync(string configurationName);
        Task<List<MailServerConfiguration>> GetAllMailConfigurationsAsync();
        Task DeleteMailConfigurationAsync(string configurationId);

        #endregion

        Task UpsertAsync<T>(T data, Expression<Func<T, bool>> filterExpression, string collectionName = "");
    }
}
