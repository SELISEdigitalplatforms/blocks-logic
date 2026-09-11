using Blocks.Genesis;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using CloudConfiguration.DomainService.Shared.Utilities;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.Enums;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private const string MaskedSecretValue = "********";

        private readonly IConfigurationRepository _configurationRepository;
        private readonly IValidator<SaveNotificatonConfigurationRequest> _notificatonConfigurationValidator;
        private readonly IValidator<SaveStorageConfigurationRequest> _storageConfigurationValidator;
        private readonly IValidator<MailConfiguration> _mailConfigurationValidator;
        private readonly IMessageClient _messageClient;
        private readonly ITenants _tenants;
        private readonly ILogger<ConfigurationService> _logger;


        public ConfigurationService(IConfigurationRepository configurationRepository,
                                    IValidator<SaveNotificatonConfigurationRequest> notificatonConfigurationValidator,
                                    IValidator<SaveStorageConfigurationRequest> storageConfigurationValidator,
                                    IValidator<MailConfiguration> mailConfigurationValidator,
                                    IMessageClient messageClient,
                                    ILogger<ConfigurationService> logger,
                                    ITenants tenants)
        {
            _configurationRepository = configurationRepository;
            _notificatonConfigurationValidator = notificatonConfigurationValidator;
            _storageConfigurationValidator = storageConfigurationValidator;
            _mailConfigurationValidator = mailConfigurationValidator;
            _messageClient = messageClient;
            _logger = logger;
            _tenants = tenants;
        }

        #region Notification

        public async Task<BaseResponse> SaveNotificationConfigurationAsync(SaveNotificatonConfigurationRequest configuration)
        {
            var validationResult = await _notificatonConfigurationValidator.ValidateAsync(configuration);

            if (!validationResult.IsValid)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var repoConfig = await MapAsync(configuration);
            await _configurationRepository.SaveNotificationConfigurationAsync(repoConfig);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request)
        {
            return await _configurationRepository.GetNotificationConfigurationsAsync(request);
        }

        public async Task<NotificationConfiguration> GetNotificatoinConfigurationAsync(GetNotificationConfigurationRequest request)
        {
            return await _configurationRepository.GetNotificationConfigurationByIdAsync(request.ItemId);
        }

        public async Task<BaseResponse> DeleteNotificationConfigurationAsync(DeleteNotificatoinConfigurationRequest request)
        {
            return await _configurationRepository.DeleteNotificationConfigurationAsync(request);
        }

        private async Task<NotificationConfiguration> MapAsync(SaveNotificatonConfigurationRequest configuration)
        {
            var repoConfig = await _configurationRepository.GetNotificationConfigurationByNameAsync(configuration.Name);

            repoConfig = repoConfig ?? new NotificationConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow, CreatedBy = BlocksContext.GetContext()?.UserId };

            repoConfig.LastUpdatedDate = DateTime.UtcNow;
            repoConfig.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            repoConfig.Name = configuration.Name;
            repoConfig.ChannelToNotify = configuration.ChannelToNotify;
            repoConfig.NotificationType = configuration.NotificationType;
            repoConfig.EnablePersistence = configuration.EnablePersistence;
            repoConfig.NotifyMethod = configuration.NotifyMethod;

            return repoConfig;
        }

        #endregion

        #region Storage

        public async Task<BaseMutationResponse> SaveStorageConfigurationAsync(SaveStorageConfigurationRequest request)
        {
            var validationResult = await _storageConfigurationValidator.ValidateAsync(request);

            if (!validationResult.IsValid)
                return new BaseMutationResponse { IsSuccess = false, Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };

            var repoConfiguration = await MappedIntoRepoConfigurationAsync(request);

            await _configurationRepository.SaveStorageConfigurationAsync(repoConfiguration);

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<CreateDefaultFolderEvent>
            {
                Payload = new CreateDefaultFolderEvent
                {
                    ItemId = repoConfiguration.ItemId,
                    ConfigurationName = repoConfiguration.Name,
                    StorageStrategy = repoConfiguration.StorageStrategy,
                    },
                ConsumerName = Constants.StorageQueue,
            });

            return new BaseMutationResponse { IsSuccess = true, ItemId = repoConfiguration.ItemId };
        }

        private async Task<StorageConfiguration> MappedIntoRepoConfigurationAsync(SaveStorageConfigurationRequest request)
        {
            var repoConfiguration = request.UpdateRequest ?
                                    await _configurationRepository.GetStorageConfigurationByIdAsync(request.ItemId ?? "") :
                                    await _configurationRepository.GetStorageConfigurationByNameAsync(request.Name);

            if (repoConfiguration == null)
            {
                repoConfiguration = new StorageConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow };
            }

            repoConfiguration.CreatedBy = BlocksContext.GetContext()?.UserId;
            repoConfiguration.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            repoConfiguration.LastUpdatedDate = DateTime.UtcNow;
            repoConfiguration.Name = request.Name;
            repoConfiguration.ConnectionString = request.ConnectionString;
            repoConfiguration.SecretKey = request.SecretKey;
            repoConfiguration.StorageStrategy = request.StorageStrategy;
            repoConfiguration.AccessKey = request.AccessKey;
            repoConfiguration.CloudStorageRegionEndPoint = request.CloudStorageRegionEndPoint;


            #region LocalStorage

            _ = StorageTypes.TryGetCategory(request.StorageStrategy, out var category);

            repoConfiguration.Host = request.Host ?? "";
            repoConfiguration.Port = request.Port ?? "";
            repoConfiguration.UserName = request.UserName ?? "";
            repoConfiguration.Password = request.Password ?? "";
            repoConfiguration.RemoteBasePath = request.RemoteBasePath ?? "";
            repoConfiguration.StorageStrategy = request.StorageStrategy ?? "";
            repoConfiguration.SftpSecretKey = category == StorageStrategyCategory.Local ? Helper.GenerateAesKey() : "";

            #endregion

            return repoConfiguration;
        }

        public static class StorageTypes
        {
            private static readonly ImmutableDictionary<string, StorageStrategyCategory> TypeToCategory =
                ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
                {
                   new KeyValuePair<string, StorageStrategyCategory>("azure", StorageStrategyCategory.Cloud),
                   new KeyValuePair<string, StorageStrategyCategory>("aws", StorageStrategyCategory.Cloud),
                   new KeyValuePair<string, StorageStrategyCategory>("sftpstorage", StorageStrategyCategory.Local)
                });

            public static bool TryGetCategory(string type, out StorageStrategyCategory category)
            {
                return TypeToCategory.TryGetValue(type.ToLower(), out category);
            }
        }

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

        public async Task<BaseResponse> DeleteStorageConfigurationAsync(string configurationName)
        {
            await _configurationRepository.DeleteStorageConfigurationByNameAsync(configurationName);

            return new BaseResponse { IsSuccess = true };
        }

        #endregion

        #region Mail

        public async Task<BaseMutationResponse> SaveMailConfigurationAsync(MailConfiguration configuration)
        {
            _logger.LogInformation("Saving mail configuration start");
            var validationResult = await _mailConfigurationValidator.ValidateAsync(configuration);

            if (!validationResult.IsValid)
            {
                _logger.LogInformation("Saving mail configuration end -- Validation Error");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var repoConfiguration = await MappedIntoMailRepoConfigurationAsync(configuration);
            await _configurationRepository.SaveMailConfigurationAsync(repoConfiguration);

            _logger.LogInformation("Saving mail configuration end -- Success");
            return new BaseMutationResponse { IsSuccess = true };
        }

        public async Task<MailConfiguration> GetMailConfigurationAsync(GetMailConfigurationRequest request)
        {
            var configuration = await _configurationRepository.GetMailConfigurationByNameAsync(request.ConfigurationName);
            configuration.AccountPassword = MaskedSecretValue;
            return configuration;
        }

        public async Task<List<MailServerConfiguration>> GetAllMailConfigurationsAsync()
        {
            var configurations = await _configurationRepository.GetAllMailConfigurationsAsync();

            foreach (var config in configurations)
            {
                config.AccountPassword = MaskedSecretValue;
            }

            return configurations;
        }

        public async Task<BaseMutationResponse> DeleteMailConfigurationAsync(DeleteMailConfigurationRequest request)
        {
            _logger.LogInformation("Deleting mail configuration start");

            var config = await _configurationRepository.GetMailConfigurationByIdAsync(request.ConfigurationId);
            if (config == null)
            {
                _logger.LogInformation("Deleting mail configuration end -- Configuration not found");

                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "ConfigurationId", "Configuration not found" }
                    }
                };
            }

            await _configurationRepository.DeleteMailConfigurationAsync(request.ConfigurationId);

            _logger.LogInformation("Deleting mail configuration end -- Success");
            return new BaseMutationResponse { IsSuccess = true };
        }

        public async Task<BaseMutationResponse> DuplicateMailConfigurationAsync(DuplicateMailConfigurationRequest request)
        {
            _logger.LogInformation("Duplicating mail configuration start");

            var config = await _configurationRepository.GetMailConfigurationByIdAsync(request.ConfigurationId);
            if (config == null)
            {
                _logger.LogInformation("Duplicating mail configuration end -- Configuration not found");

                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "ConfigurationId", "Configuration not found" }
                    }
                };
            }

            var newConfig = new MailServerConfiguration
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedBy = BlocksContext.GetContext()?.UserId,
               // OrganizationIds = config.OrganizationIds,
                Tags = config.Tags,
                Name = config.Name + " - Copy",
                Host = config.Host,
                Port = config.Port,
                EnableSSL = config.EnableSSL,
                SenderName = config.SenderName,
                SenderAddress = config.SenderAddress,
                SenderUserName = config.SenderUserName,
                AccountPassword = config.AccountPassword,
                UseDefaultCredentials = config.UseDefaultCredentials,
                SmtpClient = config.SmtpClient,
                IsDefault = config.IsDefault,
                Provider = config.Provider,
                IsInbound = config.IsInbound
            };

            await _configurationRepository.SaveMailConfigurationAsync(newConfig);

            _logger.LogInformation("Duplicating mail configuration end -- Success");
            return new BaseMutationResponse { IsSuccess = true };
        }

        private async Task<MailServerConfiguration> MappedIntoMailRepoConfigurationAsync(MailConfiguration configuration)
        {
            var repoConfiguration = await _configurationRepository.GetMailConfigurationByIdAsync(configuration.ConfigurationId);

            repoConfiguration ??= new MailServerConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow };

            repoConfiguration.LastUpdatedDate = DateTime.UtcNow;
            repoConfiguration.Host = configuration.Host;
            repoConfiguration.Port = configuration.Port;
            repoConfiguration.Name = configuration.ConfigurationName;
            repoConfiguration.SenderName = configuration.SenderName;
            repoConfiguration.SenderUserName = configuration.SenderUserName;
            repoConfiguration.SenderAddress = configuration.SenderAddress;
            repoConfiguration.AccountPassword = configuration.AccountPassword;
            repoConfiguration.EnableSSL = configuration.EnableSSL;
            repoConfiguration.CreatedBy = BlocksContext.GetContext()?.UserId ?? "";
            repoConfiguration.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "";
            repoConfiguration.IsInbound = configuration.IsInbound;
            repoConfiguration.Provider = configuration.Provider;
            return repoConfiguration;
        }

        #endregion
    }
}
