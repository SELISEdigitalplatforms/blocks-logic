using DomainService.Configuration.Validators;
using DomainService.Notification;
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
    /// inspected on the descriptors, so the wiring is asserted without standing up SignalR.
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
        [InlineData(typeof(INotifierServiceFactory), typeof(NotifierServiceFactory))]
        [InlineData(typeof(IStrategicClientProviderFactory), typeof(StrategicClientProviderFactory))]
        [InlineData(typeof(IConfigurationService), typeof(ConfigurationService))]
        [InlineData(typeof(IConfigurationRepository), typeof(ConfigurationRepository))]
        public void RegisterAllNotificationApplicationServices_RegistersTheServicesAsSingletons(
            Type service, Type implementation)
        {
            var descriptor = _services.Single(d => d.ServiceType == service);

            descriptor.ImplementationType.Should().Be(implementation);
            descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        }

        [Theory]
        [InlineData(typeof(SignalRNotificationServiceProvider))]
        [InlineData(typeof(FirebaseNotificationServiceProvider))]
        [InlineData(typeof(BroadcastReceiver))]
        [InlineData(typeof(UserSpecificReceiver))]
        [InlineData(typeof(FilterSpecificReceiver))]
        public void RegisterAllNotificationApplicationServices_RegistersEveryStrategyItsFactoriesResolve(Type strategy)
        {
            var descriptor = _services.Single(d => d.ServiceType == strategy);

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

        [Fact]
        public void RegisterAllNotificationApplicationServices_AddsSignalR()
        {
            _services.Should().Contain(d => d.ServiceType.FullName!.Contains("SignalR", StringComparison.Ordinal));
        }
    }
}
