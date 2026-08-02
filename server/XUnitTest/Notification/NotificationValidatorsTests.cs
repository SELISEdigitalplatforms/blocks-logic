using DomainService.Configuration.Validators;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Moq;
using IConfigurationRepository = DomainService.Configuration.Services.IConfigurationRepository;
using SaveConfigurationRequest = DomainService.Configuration.SaveConfigurationRequest;

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
            var withRoles = await validator.ValidateAsync(
                new NotifyRequest { ConfigurationName = "personal", Roles = [] });

            anonymous.IsValid.Should().BeFalse();
            anonymous.Errors.Should().Contain(e => e.ErrorMessage == "UserIds or Roles cannot be empty");
            withUser.IsValid.Should().BeTrue();
            withRoles.IsValid.Should().BeTrue("an empty role list is still a role selection");
        }

        [Fact]
        public async Task ConfigurationValidator_AcceptsACompleteConfiguration()
        {
            SetupConfiguration(null);

            var result = await new ConfigurationValidator(_configurationRepository.Object)
                .ValidateAsync(ValidConfiguration());

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task ConfigurationValidator_RejectsAMissingName()
        {
            SetupConfiguration(null);
            var request = ValidConfiguration();
            request.Name = string.Empty;

            var result = await new ConfigurationValidator(_configurationRepository.Object).ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Name is required.");
        }

        [Fact]
        public async Task ConfigurationValidator_RejectsANameThatIsAlreadyTaken()
        {
            SetupConfiguration(new NotificationConfiguration { Name = "welcome" });
            var request = ValidConfiguration();
            request.IsUpdateRequest = false;

            var result = await new ConfigurationValidator(_configurationRepository.Object).ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Name must be unique");
        }

        [Fact]
        public async Task ConfigurationValidator_AllowsAnUpdateToKeepItsOwnName()
        {
            SetupConfiguration(new NotificationConfiguration { Name = "welcome" });
            var request = ValidConfiguration();
            request.IsUpdateRequest = true;

            var result = await new ConfigurationValidator(_configurationRepository.Object).ValidateAsync(request);

            result.IsValid.Should().BeTrue();
            _configurationRepository.Verify(r => r.GetByNameAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ConfigurationValidator_RejectsANameLongerThanTheColumnAllows()
        {
            SetupConfiguration(null);
            var request = ValidConfiguration();
            request.Name = new string('n', 101);

            var result = await new ConfigurationValidator(_configurationRepository.Object).ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Name must not exceed 100 characters.");
        }

        [Fact]
        public async Task ConfigurationValidator_RejectsAMissingNotifyMethod()
        {
            SetupConfiguration(null);
            var request = ValidConfiguration();
            request.NotifyMethod = string.Empty;

            var result = await new ConfigurationValidator(_configurationRepository.Object).ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "NotifyMethod is required.");
        }

        [Fact]
        public async Task ConfigurationValidator_RejectsANotifyMethodLongerThanTheColumnAllows()
        {
            SetupConfiguration(null);
            var request = ValidConfiguration();
            request.NotifyMethod = new string('m', 51);

            var result = await new ConfigurationValidator(_configurationRepository.Object).ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "NotifyMethod must not exceed 50 characters.");
        }

        [Fact]
        public async Task ConfigurationValidator_RejectsChannelsAndReceiverTypesOutsideTheEnums()
        {
            SetupConfiguration(null);
            var request = ValidConfiguration();
            request.ChannelToNotify = (NotifierTypes)42;
            request.NotificationType = (NotificationReceiverTypes)42;

            var result = await new ConfigurationValidator(_configurationRepository.Object).ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Invalid channel type.");
            result.Errors.Should().Contain(e => e.ErrorMessage == "Invalid notification type.");
        }

        private static SaveConfigurationRequest ValidConfiguration() => new()
        {
            Name = "welcome",
            ChannelToNotify = NotifierTypes.SignalR,
            NotificationType = NotificationReceiverTypes.BroadcastReceiverType,
            NotifyMethod = "ReceiveNotification",
            EnablePersistence = true,
        };
    }
}
