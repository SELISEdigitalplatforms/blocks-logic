using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="StrategicClientProviderFactory"/> and
    /// <see cref="NotifierServiceFactory"/>. Both resolve a strategy out of the container by
    /// enum value, so the tests assert the mapping and the failure for an unknown value.
    /// </summary>
    public class NotificationFactoriesTests : IDisposable
    {
        private readonly HubContextDouble _hub = new();
        private readonly Mock<INotificationRepository> _repository = new();
        private readonly ServiceProvider _services;

        public NotificationFactoriesTests()
        {
            var collection = new ServiceCollection();

            collection.AddSingleton(new BroadcastReceiver(_hub.Object));
            collection.AddSingleton(new FilterSpecificReceiver(
                _repository.Object, Mock.Of<ILogger<FilterSpecificReceiver>>(), _hub.Object));
            collection.AddSingleton(new UserSpecificReceiver(
                _repository.Object, Mock.Of<ILogger<UserSpecificReceiver>>(), _hub.Object));

            collection.AddSingleton(new SignalRNotificationServiceProvider(
                Mock.Of<IStrategicClientProviderFactory>(),
                _repository.Object,
                Mock.Of<ILogger<SignalRNotificationServiceProvider>>()));
            collection.AddSingleton(new FirebaseNotificationServiceProvider(
                Mock.Of<ILogger<FirebaseNotificationServiceProvider>>(),
                _repository.Object,
                new ConfigurationBuilder().Build()));

            _services = collection.BuildServiceProvider();
        }

        public void Dispose()
        {
            _services.Dispose();
            GC.SuppressFinalize(this);
        }

        [Theory]
        [InlineData(NotificationReceiverTypes.BroadcastReceiverType, typeof(BroadcastReceiver))]
        [InlineData(NotificationReceiverTypes.FilterSpecificReceiverType, typeof(FilterSpecificReceiver))]
        [InlineData(NotificationReceiverTypes.UserSpecificReceiverType, typeof(UserSpecificReceiver))]
        public void GetStrategicClientProvider_ResolvesTheReceiverForTheType(
            NotificationReceiverTypes type, Type expected)
        {
            var factory = new StrategicClientProviderFactory(_services);

            factory.GetStrategicClientProvider(type).Should().BeOfType(expected);
        }

        [Fact]
        public void GetStrategicClientProvider_RejectsAReceiverTypeThatHasNoStrategy()
        {
            var factory = new StrategicClientProviderFactory(_services);

            var act = () => factory.GetStrategicClientProvider(NotificationReceiverTypes.NoReceiverType);

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(NotifierTypes.SignalR, typeof(SignalRNotificationServiceProvider))]
        [InlineData(NotifierTypes.Firebase, typeof(FirebaseNotificationServiceProvider))]
        public void GetNotifierServiceProvider_ResolvesTheChannelForTheType(
            NotifierTypes type, Type expected)
        {
            var factory = new NotifierServiceFactory(_services);

            factory.GetNotifierServiceProvider(type).Should().BeOfType(expected);
        }

        [Fact]
        public void GetNotifierServiceProvider_RejectsAChannelThatHasNoProvider()
        {
            var factory = new NotifierServiceFactory(_services);

            var act = () => factory.GetNotifierServiceProvider((NotifierTypes)99);

            act.Should().Throw<ArgumentException>();
        }
    }
}
