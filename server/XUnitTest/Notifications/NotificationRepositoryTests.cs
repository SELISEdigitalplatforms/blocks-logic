using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Notifications
{
    /// <summary>
    /// Unit tests for <see cref="NotificationRepository"/>. The database is resolved once in the
    /// constructor from the ambient <see cref="BlocksContext"/>, so the context is set before the
    /// repository is built. Collection names are derived from the type name, which is what most of
    /// these assertions pin.
    /// </summary>
    public class NotificationRepositoryTests : IDisposable
    {
        private const string NotificationCollection = "OfflineNotifications";

        private readonly Mock<IDbContextProvider> _provider = new();
        private readonly Mock<IMongoDatabase> _db = new();
        private readonly Mock<IBlocksSecret> _secret = new();

        public NotificationRepositoryTests()
        {
            BlocksContext.IsTestMode = true;
            _secret.SetupGet(s => s.DatabaseConnectionString).Returns("conn");
            _provider.Setup(p => p.GetDatabase(It.IsAny<string>())).Returns(_db.Object);
            _provider.Setup(p => p.GetDatabase(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(_db.Object);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetContext(string userId = "user-1", bool impersonated = false) =>
            BlocksContext.SetContext(BlocksContext.Create(
                "tenant-1", null, userId, true, null, null,
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", "tenant-1", impersonated));

        private Mock<IMongoCollection<T>> Collection<T>(string name, IEnumerable<T>? items = null)
        {
            var list = (items ?? []).ToList();
            var col = new Mock<IMongoCollection<T>>();

            col.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<FindOptions<T, T>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => Cursor(list));
            col.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(list.Count);
            col.Setup(c => c.InsertOneAsync(It.IsAny<T>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
            col.Setup(c => c.InsertManyAsync(It.IsAny<IEnumerable<T>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
            col.Setup(c => c.DeleteManyAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DeleteResult.Acknowledged(list.Count));
            col.Setup(c => c.UpdateManyAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<UpdateDefinition<T>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new UpdateResult.Acknowledged(list.Count, list.Count, null));
            col.Setup(c => c.UpdateOneAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<UpdateDefinition<T>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            _db.Setup(d => d.GetCollection<T>(name, It.IsAny<MongoCollectionSettings>())).Returns(col.Object);
            return col;
        }

        private static IAsyncCursor<T> Cursor<T>(List<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(items.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(items.Count > 0).ReturnsAsync(false);
            return cursor.Object;
        }

        private NotificationRepository Build() => new(_provider.Object, _secret.Object);

        private static OfflineNotification Notification(string id, string? forUser = null, params string[] readBy) => new()
        {
            Id = id,
            ReadByUserIds = readBy.Length == 0 ? [] : [.. readBy],
            Payload = new PayloadData { UserId = forUser },
            CreatedTime = DateTime.UtcNow,
        };

        [Fact]
        public void Constructor_ResolvesTheTenantDatabaseForANormalRequest()
        {
            SetContext();

            Build();

            _provider.Verify(p => p.GetDatabase("tenant-1"), Times.Once);
        }

        [Fact]
        public void Constructor_ResolvesTheRootDatabaseWhileImpersonating()
        {
            SetContext(impersonated: true);

            Build();

            _provider.Verify(p => p.GetDatabase("conn", "BlocksRootDb", It.IsAny<bool>()), Times.Once);
            _provider.Verify(p => p.GetDatabase(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SaveAsync_DerivesTheCollectionNameFromTheType()
        {
            SetContext();
            var col = Collection<NotificationConnection>("NotificationConnections");
            var sut = Build();

            await sut.SaveAsync(new NotificationConnection { ConnectionId = "c1" });

            col.Verify(c => c.InsertOneAsync(It.IsAny<NotificationConnection>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveAsync_HonoursAnExplicitCollectionName()
        {
            SetContext();
            var col = Collection<NotificationConnection>("CustomThings");
            var sut = Build();

            await sut.SaveAsync(new NotificationConnection { ConnectionId = "c1" }, "CustomThings");

            col.Verify(c => c.InsertOneAsync(It.IsAny<NotificationConnection>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveAsync_InsertsAListInOneCall()
        {
            SetContext();
            var col = Collection<NotificationSubscription>("NotificationSubscriptions");
            var sut = Build();

            await sut.SaveAsync(new List<NotificationSubscription>
            {
                new() { Id = "s1" },
                new() { Id = "s2" },
            });

            col.Verify(c => c.InsertManyAsync(
                It.Is<IEnumerable<NotificationSubscription>>(l => l.Count() == 2),
                It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetItemAsync_ReturnsTheFirstMatch()
        {
            SetContext();
            Collection("NotificationConnections", [new NotificationConnection { ConnectionId = "c1" }]);
            var sut = Build();

            var result = await sut.GetItemAsync<NotificationConnection>(c => c.ConnectionId == "c1");

            result.Should().NotBeNull();
            result.ConnectionId.Should().Be("c1");
        }

        [Fact]
        public async Task GetItemAsync_ReturnsNullWhenNothingMatches()
        {
            SetContext();
            Collection<NotificationConnection>("NotificationConnections");
            var sut = Build();

            (await sut.GetItemAsync<NotificationConnection>(c => c.ConnectionId == "nope")).Should().BeNull();
        }

        [Fact]
        public async Task GetItemsAsync_ReturnsEveryMatch()
        {
            SetContext();
            Collection("NotificationSubscriptions", [
                new NotificationSubscription { Id = "s1" },
                new NotificationSubscription { Id = "s2" },
            ]);
            var sut = Build();

            var result = await sut.GetItemsAsync<NotificationSubscription>(s => s.Id != null);

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task DeleteAsync_RemovesEveryMatchInTheTypeCollection()
        {
            SetContext();
            var col = Collection<NotificationSubscription>("NotificationSubscriptions");
            var sut = Build();

            await sut.DeleteAsync<NotificationSubscription>(s => s.ConnectionId == "c1");

            col.Verify(c => c.DeleteManyAsync(It.IsAny<FilterDefinition<NotificationSubscription>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateNotificationAsReadByUserIdAsync_RunsBothTheInitialiseAndTheAddToSet()
        {
            SetContext();
            var col = Collection<OfflineNotification>(NotificationCollection);
            var sut = Build();

            await sut.UpdateNotificationAsReadByUserIdAsync("user-1");

            // One pass seeds a null ReadByUserIds to an empty list so $addToSet can apply, the
            // second adds the user. Both are required, so both are asserted.
            col.Verify(c => c.UpdateManyAsync(
                It.IsAny<FilterDefinition<OfflineNotification>>(), It.IsAny<UpdateDefinition<OfflineNotification>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task UpdateNotificationAsReadByUserIdAsync_MarksASingleNotification()
        {
            SetContext();
            var col = Collection<OfflineNotification>(NotificationCollection);
            var sut = Build();

            await sut.UpdateNotificationAsReadByUserIdAsync("user-1", "n-1");

            col.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<OfflineNotification>>(), It.IsAny<UpdateDefinition<OfflineNotification>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetNotificationsAsync_ReturnsThePageWithBothCounts()
        {
            SetContext();
            Collection(NotificationCollection, [
                Notification("n1"),
                Notification("n2", readBy: "user-1"),
            ]);
            var sut = Build();

            var result = await sut.GetNotificationsAsync(new GetNotificationsRequest { Page = 0, PageSize = 10 });

            result.Notifications.Should().HaveCount(2);
            result.TotalNotificationsCount.Should().Be(2);
            result.UnReadNotificationsCount.Should().Be(2, "the mocked count answers the same for either filter");
        }

        [Fact]
        public async Task GetNotificationsAsync_FlagsWhichNotificationsTheUserHasRead()
        {
            SetContext();
            Collection(NotificationCollection, [
                Notification("n1"),
                Notification("n2", readBy: "user-1"),
            ]);
            var sut = Build();

            var result = await sut.GetNotificationsAsync(new GetNotificationsRequest { Page = 0, PageSize = 10 });

            result.Notifications.Single(n => n.Id == "n2").IsRead.Should().BeTrue();
            result.Notifications.Single(n => n.Id == "n1").IsRead.Should().BeFalse();
        }

        [Fact]
        public async Task GetNotificationsAsync_LeavesTheReadFlagAloneForAnUnreadOnlyQuery()
        {
            SetContext();
            Collection(NotificationCollection, [Notification("n1", readBy: "user-1")]);
            var sut = Build();

            var result = await sut.GetNotificationsAsync(new GetNotificationsRequest
            {
                Page = 0,
                PageSize = 10,
                IsUnreadOnly = true,
            });

            result.Notifications.Single().IsRead.Should().BeFalse("an unread-only query does not backfill the flag");
        }

        [Fact]
        public async Task GetNotificationsAsync_HandlesAnEmptyPage()
        {
            SetContext();
            Collection<OfflineNotification>(NotificationCollection);
            var sut = Build();

            var result = await sut.GetNotificationsAsync(new GetNotificationsRequest { Page = 0, PageSize = 10 });

            result.Notifications.Should().BeEmpty();
            result.TotalNotificationsCount.Should().Be(0);
        }
    }
}
