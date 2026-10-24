using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Stored notifications get a 90-day TTL index on CreatedTime, asked for once per database, and a
    /// failed index call never stops the notification from being saved.
    /// </summary>
    public sealed class OfflineNotificationTtlTests : IDisposable
    {
        // The repository remembers indexed databases for the whole process: every test uses its own name.
        private readonly string _tenant = "ttl-" + Guid.NewGuid().ToString("N");
        private readonly Mock<IMongoIndexManager<OfflineNotification>> _indexes = new();
        private readonly Mock<IMongoCollection<OfflineNotification>> _collection = new();
        private readonly NotificationRepository _repository;

        public OfflineNotificationTtlTests()
        {
            BlocksContext.IsTestMode = true;
            TestBlocksContext.Set(tenantId: _tenant);

            _collection.SetupGet(c => c.Indexes).Returns(_indexes.Object);
            _collection.Setup(c => c.InsertManyAsync(It.IsAny<IEnumerable<OfflineNotification>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.CompletedTask);

            var db = new Mock<IMongoDatabase>();
            db.SetupGet(d => d.DatabaseNamespace).Returns(new DatabaseNamespace(_tenant));
            db.Setup(d => d.GetCollection<OfflineNotification>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
              .Returns(_collection.Object);

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetDatabase(_tenant)).Returns(db.Object);

            _repository = new NotificationRepository(provider.Object, Mock.Of<IBlocksSecret>(),
                Mock.Of<ILogger<NotificationRepository>>(), new XUnitTest.TestDoubles.PassThroughLogicDataAccess());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            BlocksContext.IsTestMode = false;
        }

        private static List<OfflineNotification> Batch() =>
            [new OfflineNotification { Id = Guid.NewGuid().ToString(), CreatedTime = DateTime.UtcNow }];

        [Fact]
        public async Task Save_CreatesA90DayTtlIndexOnCreatedTime()
        {
            CreateIndexModel<OfflineNotification>? created = null;
            _indexes.Setup(i => i.CreateOneAsync(It.IsAny<CreateIndexModel<OfflineNotification>>(), It.IsAny<CreateOneIndexOptions>(), It.IsAny<CancellationToken>()))
                    .Callback<CreateIndexModel<OfflineNotification>, CreateOneIndexOptions, CancellationToken>((model, _, _) => created = model)
                    .ReturnsAsync("CreatedTime_1");

            await _repository.SaveAsync(Batch());

            created.Should().NotBeNull();
            created!.Options.ExpireAfter.Should().Be(TimeSpan.FromDays(90));
            NotificationRepository.OfflineNotificationRetention.Should().Be(TimeSpan.FromDays(90));
        }

        [Fact]
        public async Task Save_AsksForTheIndexOncePerDatabase()
        {
            _indexes.Setup(i => i.CreateOneAsync(It.IsAny<CreateIndexModel<OfflineNotification>>(), It.IsAny<CreateOneIndexOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync("CreatedTime_1");

            await _repository.SaveAsync(Batch());
            await _repository.SaveAsync(Batch());

            _indexes.Verify(i => i.CreateOneAsync(It.IsAny<CreateIndexModel<OfflineNotification>>(), It.IsAny<CreateOneIndexOptions>(), It.IsAny<CancellationToken>()), Times.Once);
            _collection.Verify(c => c.InsertManyAsync(It.IsAny<IEnumerable<OfflineNotification>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task Save_StillStoresTheNotificationWhenTheIndexCallFails()
        {
            _indexes.Setup(i => i.CreateOneAsync(It.IsAny<CreateIndexModel<OfflineNotification>>(), It.IsAny<CreateOneIndexOptions>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new MongoClientException("index options conflict"));

            await _repository.SaveAsync(Batch());

            _collection.Verify(c => c.InsertManyAsync(It.IsAny<IEnumerable<OfflineNotification>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
