using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using XUnitTest.Integration;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Integration tests for <see cref="NotificationRepository"/>. They run against the local
    /// MongoDB in a throwaway database of their own, because the read state and the paging are
    /// server side behaviour that a mocked driver would not prove.
    /// </summary>
    [Collection("Mongo integration")]
    public class NotificationRepositoryIntegrationTests : IDisposable
    {
        private const string UserId = "user-notif";

        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly string _databaseName;
        private readonly NotificationRepository _sut;

        public NotificationRepositoryIntegrationTests(MongoIntegrationFixture fixture)
        {
            TestBlocksContext.Set("tenant-notif", UserId);

            _client = fixture.Client;
            _databaseName = "blocks_logic_notif_" + Guid.NewGuid().ToString("N");
            _database = _client.GetDatabase(_databaseName);

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetDatabase(It.IsAny<string>())).Returns(_database);
            provider.Setup(p => p.GetDatabase(It.IsAny<string>(), It.IsAny<string>())).Returns(_database);

            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.DatabaseConnectionString).Returns(MongoIntegrationFixture.ConnectionString);

            _sut = new NotificationRepository(provider.Object, secret.Object);
        }

        public void Dispose()
        {
            try
            {
                _client.DropDatabase(_databaseName);
            }
            catch (MongoException)
            {
                // best effort cleanup; never mask the real test outcome
            }

            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static NotificationConnection Connection(string connectionId, string? userId = UserId) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ConnectionId = connectionId,
            UserId = userId,
            CreatedTime = DateTime.UtcNow,
        };

        private static OfflineNotification Notification(
            string id, string? userId, DateTime createdTime, List<string>? readBy = null) => new()
            {
                Id = id,
                CorrelationId = "corr-1",
                CreatedTime = createdTime,
                ReadByUserIds = readBy,
                Payload = new PayloadData
                {
                    UserId = userId,
                    NotificationType = NotificationReceiverTypes.UserSpecificReceiverType.ToString(),
                    ResponseKey = "order",
                    ResponseValue = id,
                    SubscriptionFilters = [new SubscriptionFilter { Context = "orders" }],
                },
            };

        private async Task<List<OfflineNotification>> ReadStoredNotificationsAsync() =>
            await _database.GetCollection<OfflineNotification>("OfflineNotifications")
                           .Find(FilterDefinition<OfflineNotification>.Empty)
                           .ToListAsync();

        [Fact]
        public async Task SaveAsync_StoresAnEntityInTheCollectionNamedAfterItsType()
        {
            var connection = Connection("conn-1");

            await _sut.SaveAsync(connection);

            var stored = await _sut.GetItemAsync<NotificationConnection>(c => c.ConnectionId == "conn-1");
            stored.Should().NotBeNull();
            stored.UserId.Should().Be(UserId);

            var count = await _database.GetCollection<NotificationConnection>("NotificationConnections")
                                       .CountDocumentsAsync(FilterDefinition<NotificationConnection>.Empty);
            count.Should().Be(1);
        }

        [Fact]
        public async Task SaveAsync_StoresAnEntityInTheCollectionItIsGiven()
        {
            await _sut.SaveAsync(Connection("conn-1"), "LegacyConnections");

            var fromNamedCollection = await _sut.GetItemAsync<NotificationConnection>(
                c => c.ConnectionId == "conn-1", "LegacyConnections");
            var fromDefaultCollection = await _sut.GetItemAsync<NotificationConnection>(c => c.ConnectionId == "conn-1");

            fromNamedCollection.Should().NotBeNull();
            fromDefaultCollection.Should().BeNull();
        }

        [Fact]
        public void Save_StoresAnEntityWithoutAwaiting()
        {
            _sut.Save(Connection("conn-sync"));

            _sut.GetItems<NotificationConnection>()
                .Count(c => c.ConnectionId == "conn-sync")
                .Should().Be(1);
        }

        [Fact]
        public async Task SaveAsync_StoresEveryItemOfABatch()
        {
            await _sut.SaveAsync(new List<NotificationConnection>
            {
                Connection("conn-1"),
                Connection("conn-2"),
                Connection("conn-3", "other-user"),
            });

            var mine = await _sut.GetItemsAsync<NotificationConnection>(c => c.UserId == UserId);
            var all = await _sut.GetItemsAsync<NotificationConnection>(_ => true);

            mine.Should().HaveCount(2);
            all.Should().HaveCount(3);
        }

        [Fact]
        public async Task Constructor_ReachesTheRootDatabaseForAnImpersonatedCaller()
        {
            ImpersonatedTestContext.Set("tenant-notif", UserId).Should().BeTrue();

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetDatabase(It.IsAny<string>(), It.IsAny<string>())).Returns(_database);
            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.DatabaseConnectionString).Returns(MongoIntegrationFixture.ConnectionString);

            var repository = new NotificationRepository(provider.Object, secret.Object);
            await repository.SaveAsync(Connection("conn-impersonated"));

            provider.Verify(p => p.GetDatabase(MongoIntegrationFixture.ConnectionString, "BlocksRootDb"), Times.Once);
            provider.Verify(p => p.GetDatabase(It.IsAny<string>()), Times.Never);
            (await repository.GetItemAsync<NotificationConnection>(c => c.ConnectionId == "conn-impersonated"))
                .Should().NotBeNull();
        }

        [Fact]
        public async Task GetItemAsync_ReturnsNothingWhenNoDocumentMatches()
        {
            await _sut.SaveAsync(Connection("conn-1"));

            var stored = await _sut.GetItemAsync<NotificationConnection>(c => c.ConnectionId == "conn-missing");

            stored.Should().BeNull();
        }

        [Fact]
        public async Task DeleteAsync_RemovesOnlyTheMatchingDocuments()
        {
            await _sut.SaveAsync(new List<NotificationConnection>
            {
                Connection("conn-1"),
                Connection("conn-2"),
            });

            await _sut.DeleteAsync<NotificationConnection>(c => c.ConnectionId == "conn-1");

            var remaining = await _sut.GetItemsAsync<NotificationConnection>(_ => true);
            remaining.Should().ContainSingle(c => c.ConnectionId == "conn-2");
        }

        [Fact]
        public async Task UpdateNotificationAsReadByUserIdAsync_MarksEveryNotificationTheUserCanSee()
        {
            var now = DateTime.UtcNow;
            await _sut.SaveAsync(new List<OfflineNotification>
            {
                Notification("n-mine", UserId, now),
                Notification("n-broadcast", null, now),
                Notification("n-theirs", "other-user", now),
            });

            await _sut.UpdateNotificationAsReadByUserIdAsync(UserId);

            var stored = await ReadStoredNotificationsAsync();
            stored.Single(n => n.Id == "n-mine").ReadByUserIds.Should().Equal(UserId);
            stored.Single(n => n.Id == "n-broadcast").ReadByUserIds.Should().Equal(UserId);
            stored.Single(n => n.Id == "n-theirs").ReadByUserIds.Should().BeNull(
                "another user's notification is out of scope");
        }

        [Fact]
        public async Task UpdateNotificationAsReadByUserIdAsync_KeepsTheOtherReadersAndDoesNotDuplicate()
        {
            var now = DateTime.UtcNow;
            await _sut.SaveAsync(new List<OfflineNotification>
            {
                Notification("n-partly-read", null, now, ["someone-else"]),
                Notification("n-already-read", UserId, now, [UserId]),
            });

            await _sut.UpdateNotificationAsReadByUserIdAsync(UserId);

            var stored = await ReadStoredNotificationsAsync();
            stored.Single(n => n.Id == "n-partly-read").ReadByUserIds.Should().BeEquivalentTo(["someone-else", UserId]);
            stored.Single(n => n.Id == "n-already-read").ReadByUserIds.Should().Equal(UserId);
        }

        [Fact]
        public async Task UpdateNotificationAsReadByUserIdAsync_MarksOnlyTheRequestedNotification()
        {
            var now = DateTime.UtcNow;
            await _sut.SaveAsync(new List<OfflineNotification>
            {
                Notification("n-1", UserId, now),
                Notification("n-2", UserId, now),
            });

            await _sut.UpdateNotificationAsReadByUserIdAsync(UserId, "n-1");

            var stored = await ReadStoredNotificationsAsync();
            stored.Single(n => n.Id == "n-1").ReadByUserIds.Should().Equal(UserId);
            stored.Single(n => n.Id == "n-2").ReadByUserIds.Should().BeNull();
        }

        [Fact]
        public async Task GetNotificationsAsync_ReturnsTheNewestFirstWithBothCounts()
        {
            var now = DateTime.UtcNow;
            await _sut.SaveAsync(new List<OfflineNotification>
            {
                Notification("n-old", UserId, now.AddMinutes(-10)),
                Notification("n-new", null, now),
                Notification("n-theirs", "other-user", now),
                Notification("n-read", UserId, now.AddMinutes(-5), [UserId]),
            });

            var response = await _sut.GetNotificationsAsync(new GetNotificationsRequest { Page = 0, PageSize = 10 });

            response.Notifications.Select(n => n.Id).Should().Equal("n-new", "n-read", "n-old");
            response.TotalNotificationsCount.Should().Be(3, "another user's notification is not visible");
            response.UnReadNotificationsCount.Should().Be(2);
        }

        [Fact]
        public async Task GetNotificationsAsync_StampsTheReadStateOfEachNotification()
        {
            var now = DateTime.UtcNow;
            await _sut.SaveAsync(new List<OfflineNotification>
            {
                Notification("n-unread", UserId, now),
                Notification("n-read", UserId, now.AddMinutes(-1), [UserId]),
            });

            var response = await _sut.GetNotificationsAsync(new GetNotificationsRequest { Page = 0, PageSize = 10 });

            response.Notifications.Single(n => n.Id == "n-read").IsRead.Should().BeTrue();
            response.Notifications.Single(n => n.Id == "n-unread").IsRead.Should().BeFalse();
        }

        [Fact]
        public async Task GetNotificationsAsync_ReturnsOnlyTheUnreadOnesWhenAsked()
        {
            var now = DateTime.UtcNow;
            await _sut.SaveAsync(new List<OfflineNotification>
            {
                Notification("n-unread", UserId, now),
                Notification("n-read", UserId, now.AddMinutes(-1), [UserId]),
            });

            var response = await _sut.GetNotificationsAsync(
                new GetNotificationsRequest { IsUnreadOnly = true, Page = 0, PageSize = 10 });

            response.Notifications.Should().ContainSingle(n => n.Id == "n-unread");
            response.TotalNotificationsCount.Should().Be(1);
            response.UnReadNotificationsCount.Should().Be(1);
        }

        [Fact]
        public async Task GetNotificationsAsync_PagesThroughTheNotifications()
        {
            var now = DateTime.UtcNow;
            await _sut.SaveAsync(new List<OfflineNotification>
            {
                Notification("n-1", UserId, now.AddMinutes(-1)),
                Notification("n-2", UserId, now.AddMinutes(-2)),
                Notification("n-3", UserId, now.AddMinutes(-3)),
            });

            var firstPage = await _sut.GetNotificationsAsync(new GetNotificationsRequest { Page = 0, PageSize = 2 });
            var secondPage = await _sut.GetNotificationsAsync(new GetNotificationsRequest { Page = 1, PageSize = 2 });

            firstPage.Notifications.Select(n => n.Id).Should().Equal("n-1", "n-2");
            secondPage.Notifications.Select(n => n.Id).Should().Equal("n-3");
            secondPage.TotalNotificationsCount.Should().Be(3, "the counts cover the whole result, not the page");
        }
    }
}
