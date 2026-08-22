using Common.InternalService.Shared.Utilities;
using Common.InternalService.Notification.RequestModel;
using Common.InternalService.Notification.ResponseModel;
using Common.InternalService.Notification.Entities;
using Common.InternalService.Storage.Entities;

namespace Common.InternalService.Shared.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private const string MaskedSecretValue = "********";

        private readonly IConfigurationRepository _configurationRepository;

        public ConfigurationService(IConfigurationRepository configurationRepository)
        {
            _configurationRepository = configurationRepository;
        }

        #region Notification

        public async Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request)
        {
            return await _configurationRepository.GetNotificationConfigurationsAsync(request);
        }

        public async Task<NotificationConfiguration> GetNotificatoinConfigurationAsync(GetNotificationConfigurationRequest request)
        {
            return await _configurationRepository.GetNotificationConfigurationByIdAsync(request.ItemId);
        }

        #endregion

        #region Storage

        public async Task<List<StorageConfiguration>> GetStorageConfigurationsAsync()
        {
            var configurations = await _configurationRepository.GetAllStorageConfigurationsByDateAsync();

            foreach (var configuration in configurations)
            {
                //To do masking if required
                if (configuration.StorageStrategy == "SftpStorage")
                {
                    configuration.Password = MaskedSecretValue;
                    configuration.SftpSecretKey = MaskedSecretValue;
                }
                else
                {
                    configuration.ConnectionString = Helper.GetMaskedCloudStorageRegionEndPoint(configuration.ConnectionString);
                }
            }

            return configurations;
        }

        public async Task<StorageConfiguration> GetStorageConfigurationAsync(string configurationName)
        {
            var configuration = await _configurationRepository.GetStorageConfigurationByNameAsync(configurationName);

            //To do masking if required
            if (configuration.StorageStrategy == "SftpStorage")
            {
                configuration.Password = MaskedSecretValue;
                configuration.SftpSecretKey = MaskedSecretValue;
            }
            else
            {
                configuration.ConnectionString = Helper.GetMaskedCloudStorageRegionEndPoint(configuration.ConnectionString);
            }

            return configuration;
        }

        #endregion
    }
}
