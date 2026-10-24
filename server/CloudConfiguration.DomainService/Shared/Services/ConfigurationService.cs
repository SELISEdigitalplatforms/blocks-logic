using Blocks.Genesis;
using FluentValidation;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Mail.RequestModel;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Data;
using Blocks.Genesis.Entities.Os;
using DataAccess.InternalService;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private const string MaskedSecretValue = "********";

        private readonly IConfigurationRepository _configurationRepository;
        private readonly IValidator<SaveNotificatonConfigurationRequest> _notificatonConfigurationValidator;
        private readonly IValidator<MailConfiguration> _mailConfigurationValidator;
        private readonly ITenants _tenants;
        private readonly ILogger<ConfigurationService> _logger;


        public ConfigurationService(IConfigurationRepository configurationRepository,
                                    IValidator<SaveNotificatonConfigurationRequest> notificatonConfigurationValidator,
                                    IValidator<MailConfiguration> mailConfigurationValidator,
                                    ILogger<ConfigurationService> logger,
                                    ITenants tenants)
        {
            _configurationRepository = configurationRepository;
            _notificatonConfigurationValidator = notificatonConfigurationValidator;
            _mailConfigurationValidator = mailConfigurationValidator;
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

            var stored = await _configurationRepository.GetNotificationConfigurationByNameAsync(configuration.Name);
            var repoConfig = stored ?? new NotificationConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow, CreatedBy = BlocksContext.GetContext()?.UserId };

            var refusal = NotificationConfigurationWriteRules.Apply(repoConfig, stored is null, ToWrite(configuration), NotificationConfigurationWriteRules.IsCallerRootTenant(_tenants));
            if (refusal is { } refused)
            {
                _logger.LogWarning("Notification configuration {ConfigurationName} not saved: {Error}", configuration.Name, refused.Error);
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { [refused.Field] = refused.Error } };
            }

            repoConfig.LastUpdatedDate = DateTime.UtcNow;
            repoConfig.LastUpdatedBy = BlocksContext.GetContext()?.UserId;

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
            var stored = await _configurationRepository.GetNotificationConfigurationByIdAsync(request.ItemId);
            if (NotificationConfigurationWriteRules.CheckDelete(stored, NotificationConfigurationWriteRules.IsCallerRootTenant(_tenants)) is { } refused)
            {
                _logger.LogWarning("Notification configuration {ConfigurationName} not deleted: {Error}", stored?.Name, refused.Error);
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { [refused.Field] = refused.Error } };
            }

            return await _configurationRepository.DeleteNotificationConfigurationAsync(request);
        }

        private static NotificationConfigurationWrite ToWrite(SaveNotificatonConfigurationRequest configuration) => new()
        {
            Name = configuration.Name,
            ChannelToNotify = configuration.ChannelToNotify,
            NotificationType = configuration.NotificationType,
            NotifyMethod = configuration.NotifyMethod,
            EnablePersistence = configuration.EnablePersistence,
            IsBuiltIn = configuration.IsBuiltIn,
            IsDisabled = configuration.IsDisabled,
            Description = configuration.Description,
            Importance = configuration.Importance,
            Kind = configuration.Kind,
            SourceService = configuration.SourceService,
            ConsumerAppRecommendation = configuration.ConsumerAppRecommendation,
            ConsumerAppNote = configuration.ConsumerAppNote,
        };

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
