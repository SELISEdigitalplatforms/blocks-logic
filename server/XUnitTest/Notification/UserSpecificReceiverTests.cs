using System.Linq.Expressions;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="UserSpecificReceiver"/>. The receiver turns the user ids on the
    /// payload into the live connections those users hold, so the tests assert the connection
    /// selection rather than the query itself.
    /// </summary>
    public class UserSpecificReceiverTests
    {
        private readonly Mock<INotificationRepository> _repository = new();
        private readonly HubContextDouble _hub = new();
        private readonly UserSpecificReceiver _sut;

        public UserSpecificReceiverTests()
        {
            _sut = new UserSpecificReceiver(
                _repository.Object,
                Mock.Of<ILogger<UserSpecificReceiver>>(),
                _hub.Object);
        }

        private void SetupConnections(params NotificationConnection[] connections) =>
            _repository.Setup(r => r.GetItemsAsync(
                    It.IsAny<Expression<Func<NotificationConnection, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(connections.ToList());

        [Fact]
        public async Task GetClientAsync_AddressesEveryConnectionHeldByTheRequestedUsers()
        {
            SetupConnections(
                new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" },
                new NotificationConnection { ConnectionId = "conn-2", UserId = "user-1" },
                new NotificationConnection { ConnectionId = "conn-3", UserId = "user-2" });

            var client = await _sut.GetClientAsync(new NotifierPayload { UserIds = ["user-1", "user-2"] });

            client.Should().BeSameAs(_hub.SelectedProxy.Object);
            _hub.AddressedConnectionIds.Should().Equal("conn-1", "conn-2", "conn-3");
        }

        [Fact]
        public async Task GetClientAsync_AddressesNobodyWhenTheUsersHoldNoConnection()
        {
            SetupConnections();

            var client = await _sut.GetClientAsync(new NotifierPayload { UserIds = ["user-offline"] });

            client.Should().BeSameAs(_hub.SelectedProxy.Object);
            _hub.AddressedConnectionIds.Should().BeEmpty();
        }

        [Fact]
        public async Task GetClientAsync_DoesNotRewriteThePayload()
        {
            SetupConnections(new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" });

            var payload = new NotifierPayload { UserIds = ["user-1"] };

            await _sut.GetClientAsync(payload);

            payload.UserIds.Should().Equal("user-1");
        }

        [Fact]
        public async Task GetClientAsync_QueriesTheConnectionCollectionExactlyOnce()
        {
            SetupConnections(new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" });

            await _sut.GetClientAsync(new NotifierPayload { UserIds = ["user-1", "user-2", "user-3"] });

            _repository.Verify(r => r.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationConnection, bool>>>(), It.IsAny<string>()), Times.Once);
        }
    }
}
