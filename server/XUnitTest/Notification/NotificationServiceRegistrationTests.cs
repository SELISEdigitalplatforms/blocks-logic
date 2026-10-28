using DomainService.Configuration.Validators;
using Blocks.Genesis.Notifications;
using DomainService.Notification;
using DomainService.Notification.Push;
using DomainService.Shared;
using DomainService.Utilities;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ConfigurationRepository = DomainService.Configuration.Services.ConfigurationRepository;
using ConfigurationService = DomainService.Configuration.Services.ConfigurationService;
using IConfigurationRepository = DomainService.Configuration.Services.IConfigurationRepository;
using IConfigurationService = DomainService.Configuration.Services.IConfigurationService;
using SaveConfigurationRequest = DomainService.Configuration.SaveConfigurationRequest;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="ApplicationServiceCollectionExtensions"/>. The registration is
    /// inspected on the descriptors. The genesis hub resolves <see cref="INotificationHubHandler"/>: without it every
    /// connection fails.
    /// </summary>
    public class NotificationServiceRegistrationTests
    {
        private readonly IServiceCollection _services = new ServiceCollection();

        public NotificationServiceRegistrationTests()
        {
            _services.RegisterAllNotificationApplicationServices();
        }

        private ServiceDescriptor Descriptor<TService>() =>
            _services.Single(d => d.ServiceType == typeof(TService));

        [Theory]
        [InlineData(typeof(INotificationService), typeof(NotificationService))]
        [InlineData(typeof(INotificationRepository), typeof(NotificationRepository))]
        [InlineData(typeof(INotifyService), typeof(NotifyService))]
        [InlineData(typeof(INotificationHubHandler), typeof(NotificationHubHandler))]
        [InlineData(typeof(IPushRequestHandler), typeof(PushRequestHandler))]
        [InlineData(typeof(IPushSettingsService), typeof(PushSettingsService))]
        [InlineData(typeof(IPushDeviceService), typeof(PushDeviceService))]
        [InlineData(typeof(IConfigurationService), typeof(ConfigurationService))]
        [InlineData(typeof(IConfigurationRepository), typeof(ConfigurationRepository))]
        public void RegisterAllNotificationApplicationServices_RegistersTheServicesAsSingletons(
            Type service, Type implementation)
        {
            var descriptor = _services.Single(d => d.ServiceType == service);

            descriptor.ImplementationType.Should().Be(implementation);
            descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        }

        [Fact]
        public void RegisterAllNotificationApplicationServices_RegistersTheValidatorsAsTransient()
        {
            Descriptor<IValidator<Subscription>>().ImplementationType
                .Should().Be(typeof(AddSubscriptionRequestValidator));
            Descriptor<IValidator<NotifyRequest>>().ImplementationType
                .Should().Be(typeof(NotifyRequestValidator));
            Descriptor<IValidator<SaveConfigurationRequest>>().ImplementationType
                .Should().Be(typeof(ConfigurationValidator));

            _services.Where(d => d.ServiceType.IsGenericType
                                 && d.ServiceType.GetGenericTypeDefinition() == typeof(IValidator<>))
                     .Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Transient);
        }
    }
}
