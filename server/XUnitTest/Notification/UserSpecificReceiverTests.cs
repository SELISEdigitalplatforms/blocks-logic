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

        private void SetupUsers(params NotificationUser[] users) =>
            _repository.Setup(r => r.GetItemsAsync(
                    It.IsAny<Expression<Func<NotificationUser, bool>>>(), "Users"))
                .Returns((Expression<Func<NotificationUser, bool>> filter, string _) =>
                    Task.FromResult(users.Where(filter.Compile()).ToList()));

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

        [Fact]
        public async Task GetClientAsync_ResolvesRolesToTheUsersHoldingThem()
        {
            SetupUsers(
                new NotificationUser
                {
                    ItemId = "user-1",
                    OrganizationIds = ["org-123"],
                    Roles = new Dictionary<string, List<string>> { ["org-123"] = ["admin"] }
                },
                new NotificationUser
                {
                    ItemId = "user-2",
                    OrganizationIds = ["org-123"],
                    Roles = new Dictionary<string, List<string>> { ["org-123"] = ["member"] }
                });
            SetupConnections(new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" });

            var payload = new NotifierPayload { Roles = ["admin"] };
            await _sut.GetClientAsync(payload);

            payload.UserIds.Should().Equal("user-1");
            _hub.AddressedConnectionIds.Should().Equal("conn-1");
        }

        [Fact]
        public async Task GetClientAsync_KeepsExplicitUserIdsWhenNoUserHoldsTheRequestedRole()
        {
            SetupUsers();
            SetupConnections(new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" });

            var payload = new NotifierPayload { UserIds = ["user-1"], Roles = ["missing-role"] };
            await _sut.GetClientAsync(payload);

            payload.UserIds.Should().Equal("user-1");
            _hub.AddressedConnectionIds.Should().Equal("conn-1");
        }

        [Fact]
        public async Task GetClientAsync_ResolvesRolesScopedToTheRequestedOrganizations()
        {
            SetupUsers(
                new NotificationUser
                {
                    ItemId = "user-1",
                    OrganizationIds = ["org-123"],
                    Roles = new Dictionary<string, List<string>> { ["org-123"] = ["admin"] }
                },
                new NotificationUser
                {
                    ItemId = "user-2",
                    OrganizationIds = ["org-999"],
                    Roles = new Dictionary<string, List<string>> { ["org-999"] = ["admin"] }
                });
            SetupConnections(new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" });

            var payload = new NotifierPayload { Roles = ["admin"], OrganizationIds = ["org-123"] };
            await _sut.GetClientAsync(payload);

            payload.UserIds.Should().Equal("user-1");
            _hub.AddressedConnectionIds.Should().Equal("conn-1");
        }

        [Fact]
        public async Task GetClientAsync_ResolvesEveryUserInTheRequestedOrganizationsWhenNoUserIdsOrRolesAreGiven()
        {
            SetupUsers(
                new NotificationUser { ItemId = "user-1", OrganizationIds = ["org-123"] },
                new NotificationUser { ItemId = "user-2", OrganizationIds = ["org-999"] });
            SetupConnections(new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" });

            var payload = new NotifierPayload { OrganizationIds = ["org-123"] };
            await _sut.GetClientAsync(payload);

            payload.UserIds.Should().Equal("user-1");
            _hub.AddressedConnectionIds.Should().Equal("conn-1");
        }

        [Fact]
        public async Task GetClientAsync_PrefersExplicitUserIdsOverOrganizationIdsWhenBothAreGiven()
        {
            SetupUsers(new NotificationUser { ItemId = "user-2", OrganizationIds = ["org-123"] });
            SetupConnections(new NotificationConnection { ConnectionId = "conn-1", UserId = "user-1" });

            var payload = new NotifierPayload { UserIds = ["user-1"], OrganizationIds = ["org-123"] };
            await _sut.GetClientAsync(payload);

            payload.UserIds.Should().Equal("user-1");
            _hub.AddressedConnectionIds.Should().Equal("conn-1");
        }
    }
}
