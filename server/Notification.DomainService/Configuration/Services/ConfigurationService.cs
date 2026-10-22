using Blocks.Genesis;
using FluentValidation;
using Blocks.Genesis.Entities.Os;
using DataAccess.InternalService;

namespace DomainService.Configuration.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfigurationRepository _configurationRepository;
        private readonly IValidator<SaveConfigurationRequest> _configurationValidator;
        private readonly ITenants _tenants;

        public ConfigurationService(IConfigurationRepository configurationRepository,
                                    IValidator<SaveConfigurationRequest> configurationValidator,
                                    ITenants tenants)
        {
            _configurationRepository = configurationRepository;
            _configurationValidator = configurationValidator;
            _tenants = tenants;
        }

        public async Task<BaseResponse> SaveConfigurationAsync(SaveConfigurationRequest configuration)
        {
            var validationResult = await _configurationValidator.ValidateAsync(configuration);

            if (!validationResult.IsValid)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e=>e.ErrorMessage)
                };
            }

            var stored = await _configurationRepository.GetByNameAsync(configuration.Name);
            var repoConfig = stored ?? new NotificationConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow, CreatedBy = BlocksContext.GetContext()?.UserId };

            var refusal = NotificationConfigurationWriteRules.Apply(repoConfig, stored is null, ToWrite(configuration), NotificationConfigurationWriteRules.IsCallerRootTenant(_tenants));
            if (refusal is { } refused)
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { [refused.Field] = refused.Error } };
            }

            repoConfig.LastUpdatedDate = DateTime.UtcNow;
            repoConfig.LastUpdatedBy = BlocksContext.GetContext()?.UserId;

            await _configurationRepository.SaveAsync(repoConfig);
            return new BaseResponse { IsSuccess = true };
        }

        private static NotificationConfigurationWrite ToWrite(SaveConfigurationRequest configuration) => new()
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

        public async Task<GetConfigurationsResponse> GetsAsync(GetConfigurationsRequest request)
        {
            return await _configurationRepository.GetConfigurationsAsync(request);
        }

        public async Task<NotificationConfiguration> GetAsync(GetConfigurationRequest request)
        {
            return await _configurationRepository.GetByIdAsync(request.ItemId);
        }

        public async Task<BaseResponse> DeleteAsync(DeleteConfigurationRequest request)
        {
            var stored = await _configurationRepository.GetByIdAsync(request.ItemId);
            if (NotificationConfigurationWriteRules.CheckDelete(stored, NotificationConfigurationWriteRules.IsCallerRootTenant(_tenants)) is { } refused)
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { [refused.Field] = refused.Error } };
            }

            return await _configurationRepository.DeleteConfigurationAsync(request);
        }
    }
}
