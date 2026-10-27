using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;
using DomainService.Configuration.Services;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>
    /// <see cref="NotificationService.NotifyAsync"/> with a configuration audience: the sender can only narrow it,
    /// the send goes user-specific to the final ids, and an empty result sends nothing.
    /// </summary>
    public sealed class NotificationAudienceSendTests : IDisposable
    {
        private readonly Mock<IValidator<NotifyRequest>> _validator = new();
        private readonly Mock<INotifierServiceFactory> _notifierFactory = new();
        private readonly Mock<IConfigurationRepository> _configRepo = new();
        private readonly Mock<INotificationAudienceResolver> _resolver = new();
        private readonly Mock<INotifier> _notifier = new();
        private readonly NotificationService _sut;

        private NotifyRequest? _sentRequest;
        private NotificationConfiguration? _sentConfiguration;

        public NotificationAudienceSendTests()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<NotifyRequest>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult());
            _notifierFactory.Setup(f => f.GetNotifierServiceProvider(It.IsAny<NotifierTypes>())).Returns(_notifier.Object);
            _notifier.Setup(n => n.Notify(It.IsAny<NotifyRequest>(), It.IsAny<NotificationConfiguration>()))
                     .Callback((NotifyRequest r, NotificationConfiguration c) => { _sentRequest = r; _sentConfiguration = c; })
                     .Returns(Task.CompletedTask);
            BlocksContext.IsTestMode = true;

            _sut = new NotificationService(Mock.Of<INotificationRepository>(), Mock.Of<IValidator<Subscription>>(), _validator.Object,
                NullLogger<NotificationService>.Instance, _notifierFactory.Object, _configRepo.Object, _resolver.Object);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private NotificationConfiguration Configure(NotificationAudience? audience,
            NotificationReceiverTypes type = NotificationReceiverTypes.FilterSpecificReceiverType,
            NotifierTypes channel = NotifierTypes.SignalR)
        {
            var configuration = new NotificationConfiguration
            {
                Name = "cfg", NotificationType = type, ChannelToNotify = channel, NotifyMethod = "m", EnablePersistence = true, Audience = audience,
            };
            _configRepo.Setup(c => c.GetByNameAsync("cfg")).ReturnsAsync(configuration);
            return configuration;
        }

        private void ResolveTo(params string[] userIds) =>
            _resolver.Setup(r => r.ResolveAsync(It.IsAny<NotificationAudience>(), It.IsAny<NotifierPayload>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new AudienceResolution(userIds, userIds.Length, 0));

        [Fact]
        public async Task NoAudience_SendsExactlyAsBefore()
        {
            var configuration = Configure(null);
            var request = new NotifyRequest { ConfigurationName = "cfg", Roles = ["admin"] };

            var response = await _sut.NotifyAsync(request);

            response.IsSuccess.Should().BeTrue();
            _sentConfiguration.Should().BeSameAs(configuration);
            _sentRequest!.Roles.Should().Equal("admin");
            _resolver.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Audience_SendsUserSpecificToTheFinalIds_AndClearsListsThatCouldWidenIt()
        {
            var configuration = Configure(new NotificationAudience { Roles = ["admin"] });
            ResolveTo("alice", "carol");
            var request = new NotifyRequest
            {
                ConfigurationName = "cfg", Roles = ["admin"], OrganizationIds = ["org-a"],
                SubscriptionFilters = [new SubscriptionFilter { Context = "orders" }],
            };

            await _sut.NotifyAsync(request);

            _sentRequest!.UserIds.Should().Equal("alice", "carol");
            _sentRequest.Roles.Should().BeNull();
            _sentRequest.OrganizationIds.Should().BeNull();
            _sentRequest.SubscriptionFilters.Should().BeNull();
            _sentConfiguration!.NotificationType.Should().Be(NotificationReceiverTypes.UserSpecificReceiverType);
            _sentConfiguration.NotifyMethod.Should().Be("m");
            _sentConfiguration.EnablePersistence.Should().BeTrue();
            configuration.NotificationType.Should().Be(NotificationReceiverTypes.FilterSpecificReceiverType, "the stored configuration is not changed");
        }

        [Fact]
        public async Task Audience_FirebaseChannel_PushesOnlyTheFinalIds()
        {
            Configure(new NotificationAudience { UserIds = ["alice"] }, NotificationReceiverTypes.UserSpecificReceiverType, NotifierTypes.Firebase);
            ResolveTo("alice");

            await _sut.NotifyAsync(new NotifyRequest { ConfigurationName = "cfg", UserIds = ["alice", "mallory"] });

            _notifierFactory.Verify(f => f.GetNotifierServiceProvider(NotifierTypes.Firebase));
            _sentRequest!.UserIds.Should().Equal("alice");
        }

        [Fact]
        public async Task Audience_NoOneLeft_SendsNothing_AndSucceedsWithNoReceivers()
        {
            Configure(new NotificationAudience { Roles = ["admin"] });
            ResolveTo();

            var response = await _sut.NotifyAsync(new NotifyRequest { ConfigurationName = "cfg" });

            response.IsSuccess.Should().BeTrue();
            response.Should().BeOfType<NotifyResponse>().Which.Message.Should().Be(NotifyResponse.NoReceivers);
            _notifier.Verify(n => n.Notify(It.IsAny<NotifyRequest>(), It.IsAny<NotificationConfiguration>()), Times.Never);
        }

        [Fact]
        public async Task EveryoneBroadcast_WithoutSenderLists_KeepsTheBroadcastPath()
        {
            var configuration = Configure(new NotificationAudience { EveryoneInTenant = true }, NotificationReceiverTypes.BroadcastReceiverType);

            await _sut.NotifyAsync(new NotifyRequest { ConfigurationName = "cfg" });

            _sentConfiguration.Should().BeSameAs(configuration);
            _resolver.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task EveryoneBroadcast_WithSenderLists_IsNarrowedAndSentUserSpecific()
        {
            Configure(new NotificationAudience { EveryoneInTenant = true }, NotificationReceiverTypes.BroadcastReceiverType);
            ResolveTo("bob");

            await _sut.NotifyAsync(new NotifyRequest { ConfigurationName = "cfg", UserIds = ["bob"] });

            _sentConfiguration!.NotificationType.Should().Be(NotificationReceiverTypes.UserSpecificReceiverType);
            _sentRequest!.UserIds.Should().Equal("bob");
        }

        [Fact]
        public async Task DisabledConfiguration_WithAudience_ResolvesNothing()
        {
            var configuration = Configure(new NotificationAudience { EveryoneInTenant = true });
            configuration.IsDisabled = true;

            var response = await _sut.NotifyAsync(new NotifyRequest { ConfigurationName = "cfg" });

            response.IsSuccess.Should().BeTrue();
            _resolver.VerifyNoOtherCalls();
            _notifier.VerifyNoOtherCalls();
        }
    }
}
