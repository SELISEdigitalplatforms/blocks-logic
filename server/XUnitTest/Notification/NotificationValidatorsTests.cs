using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Moq;
using IConfigurationRepository = DomainService.Configuration.Services.IConfigurationRepository;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for the notification validators. The configuration repository is mocked so the
    /// rules that depend on a stored configuration can be exercised in both directions.
    /// </summary>
    public class NotificationValidatorsTests
    {
        private readonly Mock<IConfigurationRepository> _configurationRepository = new();

        private static Subscription Subscription(string connectionId, params SubscriptionFilter[] filters) =>
            new() { Payload = { ConnectionId = connectionId, SubscriptionFilters = [.. filters] } };

        private static SubscriptionFilter Filter(string context = "orders") =>
            new() { Context = context, ActionName = "created", Value = "1" };

        private void SetupConfiguration(NotificationConfiguration? configuration) =>
            _configurationRepository.Setup(r => r.GetByNameAsync(It.IsAny<string>()))
                                    .ReturnsAsync(configuration!);

        [Fact]
        public async Task AddSubscriptionRequestValidator_AcceptsAConnectionWithAtLeastOneFilter()
        {
            var result = await new AddSubscriptionRequestValidator()
                .ValidateAsync(Subscription("conn-1", Filter()));

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task AddSubscriptionRequestValidator_RejectsAMissingConnectionId()
        {
            var result = await new AddSubscriptionRequestValidator()
                .ValidateAsync(Subscription(string.Empty, Filter()));

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Payload.ConnectionId");
        }

        [Fact]
        public async Task AddSubscriptionRequestValidator_RejectsASubscriptionWithoutFilters()
        {
            var result = await new AddSubscriptionRequestValidator().ValidateAsync(Subscription("conn-1"));

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Payload.SubscriptionFilters");
        }

        [Fact]
        public async Task NotifyRequestValidator_RejectsAMissingConfigurationName()
        {
            SetupConfiguration(null);

            var result = await new NotifyRequestValidator(_configurationRepository.Object)
                .ValidateAsync(new NotifyRequest { ConfigurationName = string.Empty });

            result.IsValid.Should().BeFalse();
            _configurationRepository.Verify(r => r.GetByNameAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NotifyRequestValidator_RejectsAConfigurationNameThatIsNotStored()
        {
            SetupConfiguration(null);

            var result = await new NotifyRequestValidator(_configurationRepository.Object)
                .ValidateAsync(new NotifyRequest { ConfigurationName = "unknown" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "no_configuration_exist");
        }

        [Fact]
        public async Task NotifyRequestValidator_AcceptsABroadcastWithoutUsersOrFilters()
        {
            SetupConfiguration(new NotificationConfiguration
            {
                Name = "broadcast",
                NotificationType = NotificationReceiverTypes.BroadcastReceiverType,
            });

            var result = await new NotifyRequestValidator(_configurationRepository.Object)
                .ValidateAsync(new NotifyRequest { ConfigurationName = "broadcast" });

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task NotifyRequestValidator_RequiresFiltersForAFilterSpecificConfiguration()
        {
            SetupConfiguration(new NotificationConfiguration
            {
                Name = "filtered",
                NotificationType = NotificationReceiverTypes.FilterSpecificReceiverType,
            });
            var validator = new NotifyRequestValidator(_configurationRepository.Object);

            var missing = await validator.ValidateAsync(
                new NotifyRequest { ConfigurationName = "filtered", SubscriptionFilters = null });
            var supplied = await validator.ValidateAsync(
                new NotifyRequest { ConfigurationName = "filtered", SubscriptionFilters = [Filter()] });

            missing.IsValid.Should().BeFalse();
            missing.Errors.Should().Contain(e => e.PropertyName == "SubscriptionFilters");
            supplied.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task NotifyRequestValidator_RequiresUsersOrRolesForAUserSpecificConfiguration()
        {
            SetupConfiguration(new NotificationConfiguration
            {
                Name = "personal",
                NotificationType = NotificationReceiverTypes.UserSpecificReceiverType,
            });
            var validator = new NotifyRequestValidator(_configurationRepository.Object);

            var anonymous = await validator.ValidateAsync(new NotifyRequest { ConfigurationName = "personal" });
            var withUser = await validator.ValidateAsync(
                new NotifyRequest { ConfigurationName = "personal", UserIds = ["user-1"] });
            var withEmptyRoles = await validator.ValidateAsync(
                new NotifyRequest { ConfigurationName = "personal", Roles = [] });
            var withRoles = await validator.ValidateAsync(
                new NotifyRequest { ConfigurationName = "personal", Roles = ["admin"] });
            var withEmptyOrg = await validator.ValidateAsync(
                new NotifyRequest { ConfigurationName = "personal", OrganizationIds = [] });
            var withOrg = await validator.ValidateAsync(
                new NotifyRequest { ConfigurationName = "personal", OrganizationIds = ["org-1"] });

            anonymous.IsValid.Should().BeFalse();
            anonymous.Errors.Should().Contain(e => e.ErrorMessage == "UserIds, Roles, and OrganizationIds cannot all be empty");
            withUser.IsValid.Should().BeTrue();
            withEmptyRoles.IsValid.Should().BeFalse("an empty role list carries no actual role selection");
            withRoles.IsValid.Should().BeTrue();
            withEmptyOrg.IsValid.Should().BeFalse("an empty organization list carries no actual organization selection");
            withOrg.IsValid.Should().BeTrue();
        }

    }
}
