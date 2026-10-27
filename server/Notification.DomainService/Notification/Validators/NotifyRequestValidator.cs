using DomainService.Configuration.Services;
using DomainService.Shared;
using FluentValidation;
using Blocks.Genesis.Entities.Os;

namespace DomainService.Notification
{
    public class NotifyRequestValidator : AbstractValidator<NotifyRequest>
    {
        private readonly IConfigurationRepository _configurationRepository;
        // The config found for THIS request lives in the validation context, never in a shared field:
        // a static field let two Notify calls at the same time check each other's receiver rules.
        private const string ConfigKey = "NotificationConfiguration";

        public NotifyRequestValidator(IConfigurationRepository configurationRepository)
        {
            _configurationRepository = configurationRepository;

            RuleFor(p => p.ConfigurationName)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .NotEmpty()
                .MustAsync(IsConfigurationExist).WithMessage("no_configuration_exist");

            RuleFor(p => p.SubscriptionFilters)
                   .Cascade(CascadeMode.Stop)
                   .NotNull()
                   .When((p, context) => ConfigOf(context)?.NotificationType == NotificationReceiverTypes.FilterSpecificReceiverType
                                         && !HasAudience(context));

            RuleFor(p => p)
                .Cascade(CascadeMode.Stop)
                .Must(UserOrRoleShouldExist)
                .When((p, context) => ConfigOf(context)?.NotificationType == NotificationReceiverTypes.UserSpecificReceiverType
                                      && !HasAudience(context)).WithMessage("UserIds, Roles, and OrganizationIds cannot all be empty");
        }

        private async Task<bool> IsConfigurationExist(NotifyRequest notifyRequest, string configurationName, ValidationContext<NotifyRequest> context, CancellationToken cancellationToken)
        {
            var config = await _configurationRepository.GetByNameAsync(configurationName);
            context.RootContextData[ConfigKey] = config;
            return config != null;
        }

        private static NotificationConfiguration? ConfigOf(ValidationContext<NotifyRequest> context) =>
            context.RootContextData.TryGetValue(ConfigKey, out var config) ? config as NotificationConfiguration : null;

        // The configuration's audience names the receivers, so the sender's lists are optional (they can only narrow it).
        private static bool HasAudience(ValidationContext<NotifyRequest> context) => ConfigOf(context)?.Audience?.IsSet() == true;

        private static bool UserOrRoleShouldExist(NotifyRequest notifyRequest)
        {
            bool hasUserIds = notifyRequest.UserIds != null && notifyRequest.UserIds.Count != 0;
            bool hasRoles = notifyRequest.Roles != null && notifyRequest.Roles.Count != 0;
            bool hasOrg = notifyRequest.OrganizationIds != null && notifyRequest.OrganizationIds.Count != 0;
            return hasUserIds || hasRoles || hasOrg ;
        }
    }
}
