using System.Linq.Expressions;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="FilterSpecificReceiver"/>. The repository is mocked, so the
    /// tests assert which connections the receiver selects for a set of subscription filters
    /// and which user ids it writes back onto the payload.
    /// </summary>
    public class FilterSpecificReceiverTests
    {
        private readonly Mock<INotificationRepository> _repository = new();
        private readonly HubContextDouble _hub = new();
        private readonly FilterSpecificReceiver _sut;

        public FilterSpecificReceiverTests()
        {
            _sut = new FilterSpecificReceiver(
                _repository.Object,
                Mock.Of<ILogger<FilterSpecificReceiver>>(),
                _hub.Object);
        }

        private static SubscriptionFilter Filter(string context, string action = "", string value = "") =>
            new() { Context = context, ActionName = action, Value = value };

        private static NotificationSubscription Subscription(string connectionId, string? userId = null) =>
            new() { Id = Guid.NewGuid().ToString(), ConnectionId = connectionId, UserId = userId };

        private void SetupSubscriptions(params NotificationSubscription[] subscriptions) =>
            _repository.Setup(r => r.GetItemsAsync(
                    It.IsAny<Expression<Func<NotificationSubscription, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(subscriptions.ToList());

        private void SetupConnections(params NotificationConnection[] connections) =>
            _repository.Setup(r => r.GetItemsAsync(
                    It.IsAny<Expression<Func<NotificationConnection, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(connections.ToList());

        [Fact]
        public async Task GetClientAsync_AddressesTheConnectionsSubscribedToTheFilters()
        {
            SetupSubscriptions(Subscription("conn-1", "user-1"), Subscription("conn-2", "user-2"));
            SetupConnections(
                new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" },
                new NotificationConnection { ConnectionId = "conn-2", UserId = "user-2" });

            var payload = new NotifierPayload { SubscriptionFilters = [Filter("orders", "created", "1")] };

            var client = await _sut.GetClientAsync(payload);

            client.Should().BeSameAs(_hub.SelectedProxy.Object);
            _hub.AddressedConnectionIds.Should().Equal("conn-1", "conn-2");
        }

        [Fact]
        public async Task GetClientAsync_QueriesOncePerFilterAndKeepsEveryMatchedConnection()
        {
            _repository.SetupSequence(r => r.GetItemsAsync(
                    It.IsAny<Expression<Func<NotificationSubscription, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync([Subscription("conn-1", "user-1")])
                .ReturnsAsync([Subscription("conn-2", "user-2")]);
            SetupConnections();

            var payload = new NotifierPayload
            {
                SubscriptionFilters = [Filter("orders", "created", "1"), Filter("orders", "updated", "2")],
            };

            await _sut.GetClientAsync(payload);

            _repository.Verify(r => r.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>(), It.IsAny<string>()), Times.Exactly(2));
            _hub.AddressedConnectionIds.Should().Equal("conn-1", "conn-2");
        }

        [Fact]
        public async Task GetClientAsync_OverwritesThePayloadUserIdsWithTheOwnersOfTheConnections()
        {
            SetupSubscriptions(Subscription("conn-1"), Subscription("conn-2"));
            SetupConnections(
                new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" },
                new NotificationConnection { ConnectionId = "conn-2", UserId = "user-2" },
                new NotificationConnection { ConnectionId = "conn-3", UserId = "user-1" });

            var payload = new NotifierPayload
            {
                UserIds = ["user-stale"],
                SubscriptionFilters = [Filter("orders")],
            };

            await _sut.GetClientAsync(payload);

            payload.UserIds.Should().Equal("user-1", "user-2");
        }

        [Fact]
        public async Task GetClientAsync_LeavesThePayloadUserIdsAloneWhenNoConnectionIsSelected()
        {
            SetupSubscriptions();
            SetupConnections(new NotificationConnection { ConnectionId = "conn-9", UserId = "user-9" });

            var payload = new NotifierPayload
            {
                UserIds = ["user-original"],
                SubscriptionFilters = [Filter("orders")],
            };

            var client = await _sut.GetClientAsync(payload);

            payload.UserIds.Should().Equal("user-original");
            _hub.AddressedConnectionIds.Should().BeEmpty();
            client.Should().BeSameAs(_hub.SelectedProxy.Object);

            // With no connection ids there is nothing to resolve owners for, so the
            // connection collection is never queried.
            _repository.Verify(r => r.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationConnection, bool>>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetClientAsync_AddressesNobodyWhenThePayloadCarriesNoFilters()
        {
            SetupConnections();

            var payload = new NotifierPayload { SubscriptionFilters = [] };

            await _sut.GetClientAsync(payload);

            _hub.AddressedConnectionIds.Should().BeEmpty();
            _repository.Verify(r => r.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetClientAsync_ThrowsWhenThePayloadHasNoFilterCollectionAtAll()
        {
            // Documents current behaviour: a filter specific notification without a
            // SubscriptionFilters collection is dereferenced without a null check.
            var payload = new NotifierPayload { SubscriptionFilters = null };

            await Assert.ThrowsAsync<NullReferenceException>(() => _sut.GetClientAsync(payload));
        }

        [Fact]
        public async Task GetConnectionIdsByFilter_ReturnsEachConnectionOnlyOnce()
        {
            SetupSubscriptions(
                Subscription("conn-1", "user-1"),
                Subscription("conn-1", "user-1"),
                Subscription("conn-2", "user-2"));

            var connectionIds = await _sut.GetConnectionIdsByFilter(Filter("orders", "created", "1"));

            connectionIds.Should().Equal("conn-1", "conn-2");
        }

        [Fact]
        public async Task GetConnectionIdsByFilter_ReturnsAnEmptyListWhenNothingIsSubscribed()
        {
            SetupSubscriptions();

            var connectionIds = await _sut.GetConnectionIdsByFilter(Filter("orders"));

            connectionIds.Should().BeEmpty();
        }
    }
}
