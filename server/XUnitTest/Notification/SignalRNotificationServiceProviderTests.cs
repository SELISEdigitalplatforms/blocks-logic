using System.Linq.Expressions;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="SignalRNotificationServiceProvider"/>. The strategic client
    /// provider, the client proxy and the repository are mocked, so the tests assert what is
    /// pushed over the hub and what is persisted for offline delivery.
    /// </summary>
    public class SignalRNotificationServiceProviderTests
    {
        private readonly Mock<IStrategicClientProviderFactory> _clientFactory = new();
        private readonly Mock<IStrategicClientProvider> _clientProvider = new();
        private readonly Mock<IClientProxy> _clientProxy = new();
        private readonly Mock<INotificationRepository> _repository = new();
        private readonly List<List<OfflineNotification>> _savedBatches = [];
        private readonly SignalRNotificationServiceProvider _sut;

        public SignalRNotificationServiceProviderTests()
        {
            _clientFactory.Setup(f => f.GetStrategicClientProvider(It.IsAny<NotificationReceiverTypes>()))
                          .Returns(_clientProvider.Object);
            _clientProvider.Setup(p => p.GetClientAsync(It.IsAny<NotifierPayload>()))
                           .ReturnsAsync(_clientProxy.Object);
            _clientProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask);
            _repository.Setup(r => r.SaveAsync(It.IsAny<List<OfflineNotification>>()))
                       .Callback<List<OfflineNotification>>(batch => _savedBatches.Add(batch))
                       .Returns(Task.CompletedTask);
            SetupSubscriptions();

            _sut = new SignalRNotificationServiceProvider(
                _clientFactory.Object,
                _repository.Object,
                Mock.Of<ILogger<SignalRNotificationServiceProvider>>());
        }

        private void SetupSubscriptions(params NotificationSubscription[] subscriptions) =>
            _repository.Setup(r => r.GetItemsAsync(
                    It.IsAny<Expression<Func<NotificationSubscription, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(subscriptions.ToList());

        private static NotificationConfiguration Configuration(
            NotificationReceiverTypes type = NotificationReceiverTypes.UserSpecificReceiverType,
            bool enablePersistence = false,
            string notifyMethod = "ReceiveNotification") =>
            new()
            {
                Name = "cfg",
                ChannelToNotify = NotifierTypes.SignalR,
                NotificationType = type,
                NotifyMethod = notifyMethod,
                EnablePersistence = enablePersistence,
            };

        private List<OfflineNotification> SavedNotifications => [.. _savedBatches.SelectMany(b => b)];

        [Fact]
        public async Task Notify_PushesTheRequestToTheClientsUnderTheConfiguredMethodName()
        {
            var request = new NotifyRequest { ConfigurationName = "cfg", ResponseKey = "key", ResponseValue = "value" };

            await _sut.Notify(request, Configuration(notifyMethod: "OrderChanged"));

            _clientProxy.Verify(p => p.SendCoreAsync(
                "OrderChanged",
                It.Is<object[]>(args => args.Length == 1 && ReferenceEquals(args[0], request)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Notify_ResolvesTheClientProviderForTheConfiguredReceiverType()
        {
            var request = new NotifyRequest { ConfigurationName = "cfg" };

            await _sut.Notify(request, Configuration(NotificationReceiverTypes.BroadcastReceiverType));

            _clientFactory.Verify(f => f.GetStrategicClientProvider(
                NotificationReceiverTypes.BroadcastReceiverType), Times.Once);
            _clientProvider.Verify(p => p.GetClientAsync(request), Times.Once);
        }

        [Fact]
        public async Task Notify_PersistsNothingWhenPersistenceIsDisabled()
        {
            var request = new NotifyRequest { ConfigurationName = "cfg", UserIds = ["user-1"] };

            await _sut.Notify(request, Configuration(enablePersistence: false));

            _repository.Verify(r => r.SaveAsync(It.IsAny<List<OfflineNotification>>()), Times.Never);
        }

        [Fact]
        public async Task Notify_PersistsOneNotificationPerRecipientUnderASharedCorrelationId()
        {
            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                ResponseKey = "order",
                ResponseValue = "42",
                UserIds = ["user-1", "user-2"],
                DenormalizedPayload = "{\"total\":10}",
            };

            await _sut.Notify(request, Configuration(NotificationReceiverTypes.UserSpecificReceiverType, true));

            var saved = SavedNotifications;
            saved.Should().HaveCount(2);
            saved.Select(n => n.Payload.UserId).Should().Equal("user-1", "user-2");
            saved.Select(n => n.CorrelationId).Distinct().Should().ContainSingle();
            saved.Select(n => n.Id).Should().OnlyHaveUniqueItems();
            saved.Should().OnlyContain(n => n.Payload.ResponseKey == "order" && n.Payload.ResponseValue == "42");
            saved.Should().OnlyContain(n =>
                n.Payload.NotificationType == NotificationReceiverTypes.UserSpecificReceiverType.ToString());
            saved.Should().OnlyContain(n => n.CreatedTime != default);
        }

        [Fact]
        public async Task Notify_PersistsAnEmptyBatchWhenNoRecipientIsResolved()
        {
            var request = new NotifyRequest { ConfigurationName = "cfg", UserIds = null };

            await _sut.Notify(request, Configuration(enablePersistence: true));

            _repository.Verify(r => r.SaveAsync(It.IsAny<List<OfflineNotification>>()), Times.Once);
            SavedNotifications.Should().BeEmpty();
        }

        [Fact]
        public async Task Notify_ReplacesTheRecipientsWithTheSubscribersOfTheFilters()
        {
            SetupSubscriptions(
                new NotificationSubscription { Id = "s1", ConnectionId = "conn-1", UserId = "user-a" },
                new NotificationSubscription { Id = "s2", ConnectionId = "conn-2", UserId = "user-b" });

            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                UserIds = ["user-stale"],
                SubscriptionFilters = [new SubscriptionFilter { Context = "orders", ActionName = "created" }],
            };

            await _sut.Notify(request, Configuration(NotificationReceiverTypes.FilterSpecificReceiverType, true));

            request.UserIds.Should().Equal("user-a", "user-b");
            SavedNotifications.Select(n => n.Payload.UserId).Should().Equal("user-a", "user-b");
        }

        [Fact]
        public async Task Notify_KeepsTheOriginalRecipientsWhenNoSubscriberMatchesTheFilters()
        {
            SetupSubscriptions();

            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                UserIds = ["user-original"],
                SubscriptionFilters = [new SubscriptionFilter { Context = "orders" }],
            };

            await _sut.Notify(request, Configuration(NotificationReceiverTypes.FilterSpecificReceiverType, true));

            request.UserIds.Should().Equal("user-original");
        }

        [Fact]
        public async Task Notify_DropsSubscribersThatHaveNoUserId()
        {
            SetupSubscriptions(
                new NotificationSubscription { Id = "s1", ConnectionId = "conn-1", UserId = null },
                new NotificationSubscription { Id = "s2", ConnectionId = "conn-2", UserId = "user-b" });

            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                SubscriptionFilters = [new SubscriptionFilter { Context = "orders" }],
            };

            await _sut.Notify(request, Configuration(NotificationReceiverTypes.FilterSpecificReceiverType, true));

            request.UserIds.Should().Equal("user-b");
        }

        [Fact]
        public async Task Notify_QueriesTheSubscribersOncePerFilter()
        {
            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                UserIds = ["user-1"],
                SubscriptionFilters =
                [
                    new SubscriptionFilter { Context = "orders", ActionName = "created" },
                    new SubscriptionFilter { Context = "orders", ActionName = "updated" },
                ],
            };

            await _sut.Notify(request, Configuration(NotificationReceiverTypes.FilterSpecificReceiverType, true));

            _repository.Verify(r => r.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>(), It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public async Task Notify_SplitsAnAudienceLargerThanOneBatchIntoSeveralSaves()
        {
            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                UserIds = [.. Enumerable.Range(0, 1501).Select(i => $"user-{i}")],
            };

            await _sut.Notify(request, Configuration(enablePersistence: true));

            _savedBatches.Should().HaveCount(2);
            _savedBatches[0].Should().HaveCount(1500);
            _savedBatches[1].Should().HaveCount(1);
            SavedNotifications.Select(n => n.Payload.UserId).Should().OnlyHaveUniqueItems();
            SavedNotifications.Should().HaveCount(1501);
        }

        [Fact]
        public async Task Notify_SavesAnAudienceThatFitsInOneBatchWithASingleCall()
        {
            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                UserIds = [.. Enumerable.Range(0, 1500).Select(i => $"user-{i}")],
            };

            await _sut.Notify(request, Configuration(enablePersistence: true));

            _savedBatches.Should().ContainSingle();
            _savedBatches[0].Should().HaveCount(1500);
        }

        [Fact]
        public async Task Notify_StoresTheDenormalizedPayloadVerbatimByDefault()
        {
            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                UserIds = ["user-1"],
                DenormalizedPayload = "{\"total\":10}",
                SaveDenormalizedPayloadAsAnObject = false,
            };

            await _sut.Notify(request, Configuration(enablePersistence: true));

            ((object)SavedNotifications.Single().DenormalizedPayload).Should().Be("{\"total\":10}");
        }

        [Fact]
        public async Task Notify_StoresTheDenormalizedPayloadAsAnObjectWhenAsked()
        {
            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                UserIds = ["user-1"],
                DenormalizedPayload = "{\"total\":10}",
                SaveDenormalizedPayloadAsAnObject = true,
            };

            await _sut.Notify(request, Configuration(enablePersistence: true));

            object stored = SavedNotifications.Single().DenormalizedPayload;
            stored.Should().NotBeNull();
            stored.Should().NotBe("{\"total\":10}", "the payload is parsed instead of stored as text");
        }

        [Fact]
        public async Task Notify_StoresTheDenormalizedPayloadVerbatimWhenItIsEmpty()
        {
            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                UserIds = ["user-1"],
                DenormalizedPayload = string.Empty,
                SaveDenormalizedPayloadAsAnObject = true,
            };

            await _sut.Notify(request, Configuration(enablePersistence: true));

            ((object)SavedNotifications.Single().DenormalizedPayload).Should().Be(string.Empty);
        }
    }
}
