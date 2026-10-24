using System.Linq.Expressions;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notification
{
    /// <summary>
    /// A root tenant user working in project P (tenant impersonation) connects to the hub; then a sender
    /// sends to that user. Each database is an in-memory list, so the tests prove the hub write and the
    /// sender lookup meet in the same database. The Blocks services (localization export, migration)
    /// send with the root key; a console action can also send with the impersonated token.
    /// </summary>
    public sealed class ImpersonatedDeliveryTests : IDisposable
    {
        private const string Root = "root";
        private const string Project = "project-p";
        private const string RootUser = "root-user-1";
        private const string ConnectionId = "conn-1";

        private readonly Dictionary<string, List<object>> _dbs = new()
        {
            [Root] = [],
            [Project] = [],
        };
        private readonly HubContextDouble _hub = new();
        private readonly NotificationRepository _repository;

        public ImpersonatedDeliveryTests()
        {
            BlocksContext.IsTestMode = true;
            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.DatabaseConnectionString).Returns("conn");
            secret.SetupGet(s => s.RootDatabaseName).Returns("BlocksRootDb");

            // The root tenant id and the root DB name are the same database, as in Genesis.
            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetDatabase(It.IsAny<string>())).Returns((string tenantId) => Database(tenantId));
            provider.Setup(p => p.GetDatabase("conn", "BlocksRootDb", It.IsAny<bool>())).Returns(Database(Root));

            _repository = new NotificationRepository(provider.Object, secret.Object,
                Mock.Of<ILogger<NotificationRepository>>(), new XUnitTest.TestDoubles.PassThroughLogicDataAccess());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            BlocksContext.IsTestMode = false;
        }

        private IMongoDatabase Database(string name)
        {
            var store = _dbs[name];
            var db = new Mock<IMongoDatabase>();
            db.Setup(d => d.GetCollection<NotificationConnection>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
              .Returns(() => Collection<NotificationConnection>(store));
            db.Setup(d => d.GetCollection<NotificationSubscription>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
              .Returns(() => Collection<NotificationSubscription>(store));
            return db.Object;
        }

        // Find returns every record of the type in that database: the tests are about which database
        // is used, and each database holds only this test's records.
        private static IMongoCollection<T> Collection<T>(List<object> store)
        {
            var col = new Mock<IMongoCollection<T>>();
            col.Setup(c => c.InsertOneAsync(It.IsAny<T>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
               .Callback<T, InsertOneOptions, CancellationToken>((item, _, _) => store.Add(item!))
               .Returns(Task.CompletedTask);
            col.Setup(c => c.InsertManyAsync(It.IsAny<IEnumerable<T>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()))
               .Callback<IEnumerable<T>, InsertManyOptions, CancellationToken>((items, _, _) => store.AddRange(items.Cast<object>()))
               .Returns(Task.CompletedTask);
            col.Setup(c => c.DeleteManyAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<CancellationToken>()))
               .Callback(() => store.RemoveAll(item => item is T))
               .ReturnsAsync(new DeleteResult.Acknowledged(1));
            col.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<FindOptions<T, T>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => Cursor(store.OfType<T>().ToList()));
            return col.Object;
        }

        private static IAsyncCursor<T> Cursor<T>(List<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(items.Count > 0).ReturnsAsync(false);
            return cursor.Object;
        }

        private async Task ConnectWhileImpersonating()
        {
            TestBlocksContext.SetImpersonated(Project, Root, RootUser);
            await _repository.SaveAsync(new NotificationConnection { Id = "c", ConnectionId = ConnectionId, UserId = RootUser });
            await _repository.SaveAsync(new List<NotificationSubscription>
            {
                new() { Id = "s", ConnectionId = ConnectionId, UserId = RootUser, Context = "uilm", ActionName = "export", Value = "" },
            });
        }

        private static void SendWithRootKey() => TestBlocksContext.Set(Root);

        private static void SendWithImpersonatedToken() => TestBlocksContext.SetImpersonated(Project, Root, RootUser);

        private static void SendWithProjectKey() => TestBlocksContext.Set(Project);

        private async Task<IReadOnlyList<string>?> SendToUser()
        {
            var receiver = new UserSpecificReceiver(_repository, Mock.Of<ILogger<UserSpecificReceiver>>(), _hub.Object);
            await receiver.GetClientAsync(new NotifierPayload { UserIds = [RootUser] });
            return _hub.AddressedConnectionIds;
        }

        private async Task<IReadOnlyList<string>?> SendToFilter()
        {
            var receiver = new FilterSpecificReceiver(_repository, Mock.Of<ILogger<FilterSpecificReceiver>>(), _hub.Object);
            await receiver.GetClientAsync(new NotifierPayload
            {
                SubscriptionFilters = [new SubscriptionFilter { Context = "uilm", ActionName = "export", Value = "file-1" }],
            });
            return _hub.AddressedConnectionIds;
        }

        [Fact]
        public async Task TheHubStoresTheImpersonatedConnectionInTheRootDatabaseOnly()
        {
            await ConnectWhileImpersonating();

            _dbs[Root].Should().HaveCount(2);
            _dbs[Project].Should().BeEmpty();
        }

        [Fact]
        public async Task AUserNotificationSentWithTheRootKeyReachesTheImpersonatedUser()
        {
            await ConnectWhileImpersonating();
            SendWithRootKey();

            (await SendToUser()).Should().Equal(ConnectionId);
        }

        [Fact]
        public async Task AUserNotificationSentWithTheImpersonatedTokenReachesTheImpersonatedUser()
        {
            await ConnectWhileImpersonating();
            SendWithImpersonatedToken();

            (await SendToUser()).Should().Equal(ConnectionId);
        }

        [Fact]
        public async Task AUserNotificationSentWithTheProjectKeyReachesTheRootUserThroughTheProjectUserGroup()
        {
            // blocks-data, blocks-iam and blocks-release send a console user's own action with the project key.
            // The connection row is in root, so the project DB has none; the project user group carries it.
            await ConnectWhileImpersonating();
            SendWithProjectKey();
            var receiver = new UserSpecificReceiver(_repository, Mock.Of<ILogger<UserSpecificReceiver>>(), _hub.Object);

            var client = await receiver.GetClientAsync(new NotifierPayload { UserIds = [RootUser] });
            await client.SendAsync("notify", "payload");

            _hub.AddressedConnectionIds.Should().BeEmpty();
            _hub.AddressedGroups.Should().Equal("tenant:project-p:user:root-user-1");
            _hub.GroupsProxy.Verify(p => p.SendCoreAsync("notify", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AUserNotificationSentWithTheImpersonatedTokenUsesNoProjectUserGroup()
        {
            // The impersonated send already finds the connection in root; a group too would deliver twice.
            await ConnectWhileImpersonating();
            SendWithImpersonatedToken();

            await SendToUser();

            _hub.Clients.Verify(c => c.Groups(It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }

        [Fact]
        public async Task AUserNotificationSentWithTheRootKeyUsesNoProjectUserGroupOfAnotherTenant()
        {
            await ConnectWhileImpersonating();
            SendWithRootKey();

            await SendToUser();

            _hub.AddressedGroups?.Should().OnlyContain(g => g.StartsWith("tenant:root:", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AFilterNotificationSentWithTheRootKeyReachesTheImpersonatedSubscriber()
        {
            await ConnectWhileImpersonating();
            SendWithRootKey();

            (await SendToFilter()).Should().Equal(ConnectionId);
        }

        [Fact]
        public async Task AFilterNotificationSentWithTheImpersonatedTokenReachesTheImpersonatedSubscriber()
        {
            await ConnectWhileImpersonating();
            SendWithImpersonatedToken();

            (await SendToFilter()).Should().Equal(ConnectionId);
        }

        [Fact]
        public async Task DisconnectingWhileImpersonatedRemovesTheRowsFromTheRootDatabase()
        {
            await ConnectWhileImpersonating();
            SendWithImpersonatedToken(); // the hub disconnect runs with the same impersonated token

            await _repository.DeleteAsync<NotificationConnection>(c => c.ConnectionId == ConnectionId);
            await _repository.DeleteAsync<NotificationSubscription>(s => s.ConnectionId == ConnectionId);

            _dbs[Root].Should().BeEmpty();
        }

        [Theory]
        [InlineData(false, "tenant:root")]
        [InlineData(true, "tenant:project-p")]
        public async Task ABroadcastAddressesAGroupTheImpersonatedConnectionJoined(bool impersonatedToken, string group)
        {
            // NotificationHubTests pins that an impersonated connection joins tenant:project-p and tenant:root.
            if (impersonatedToken) SendWithImpersonatedToken(); else SendWithRootKey();

            await new BroadcastReceiver(_hub.Object).GetClientAsync(new NotifierPayload());

            _hub.AddressedGroup.Should().Be(group);
        }
    }
}
