using DomainService.Configuration.Services;
using DomainService.Entities;
using DomainService.Shared;
using FluentValidation;

namespace DomainService.Notification
{
    public class NotifyRequestValidator : AbstractValidator<NotifyRequest>
    {
        private readonly IConfigurationRepository _configurationRepository;
        private static NotificationConfiguration? _config;

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
                   .When(p => _config?.NotificationType == NotificationReceiverTypes.FilterSpecificReceiverType);

            RuleFor(p => p)
                .Cascade(CascadeMode.Stop)
                .Must(UserOrRoleShouldExist)
                .When(p => _config?.NotificationType == NotificationReceiverTypes.UserSpecificReceiverType).WithMessage("UserIds, Roles, and OrganizationIds cannot all be empty");
        }

        private async Task<bool> IsConfigurationExist(string configurationName, CancellationToken cancellationToken)
        {
            _config = await _configurationRepository.GetByNameAsync(configurationName);
            return _config != null;
        }

        private bool UserOrRoleShouldExist(NotifyRequest notifyRequest)
        {
            bool hasUserIds = notifyRequest.UserIds != null && notifyRequest.UserIds.Count != 0;
            bool hasRoles = notifyRequest.Roles != null && notifyRequest.Roles.Count != 0;
            bool hasOrg = notifyRequest.OrganizationIds != null && notifyRequest.OrganizationIds.Count != 0;
            return hasUserIds || hasRoles || hasOrg ;
        }
    }
}
