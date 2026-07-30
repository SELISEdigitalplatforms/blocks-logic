using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Migration;
using DomainService.Migration.Entities;
using DomainService.Migration.Services;
using DomainService.Shared;
using DomainService.Subscription.Services;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Identifier
{
    public class MigrationRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IBlocksSecret> _blocksSecret = new();
        private readonly Mock<IMongoCollection<MigrationTracker>> _trackers = new();

        private readonly Mock<IMongoDatabase> _sourceDb = new();
        private readonly Mock<IMongoDatabase> _targetDb = new();
        private readonly Mock<IMongoCollection<BsonDocument>> _sourceCollection = new();
        private readonly Mock<IMongoCollection<BsonDocument>> _targetCollection = new();

        public MigrationRepositoryTests()
        {
            _blocksSecret.SetupGet(s => s.DatabaseConnectionString).Returns("mongodb://localhost");
            _dbContextProvider.Setup(p => p.GetCollection<MigrationTracker>(IdentifierConstants.MigrationTrackerCollectionName))
                .Returns(_trackers.Object);
            _dbContextProvider.Setup(p => p.GetDatabase("SOURCE-1")).Returns(_sourceDb.Object);
            _dbContextProvider.Setup(p => p.GetDatabase("TARGET-1")).Returns(_targetDb.Object);
            _dbContextProvider.Setup(p => p.GetDatabase("mongodb://localhost", "BlocksConfiguration", It.IsAny<bool>()))
                .Returns(_sourceDb.Object);
            _sourceDb.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null)).Returns(_sourceCollection.Object);
            _targetDb.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null)).Returns(_targetCollection.Object);
        }

        private MigrationRepository CreateRepository() => new(_dbContextProvider.Object, _blocksSecret.Object);

        private static IAsyncCursor<T> NewCursor<T>(List<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(items.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(items.Count > 0).ReturnsAsync(false);
            return cursor.Object;
        }

        private static void SetupFind<T>(Mock<IMongoCollection<T>> collection, params T[] items)
        {
            collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<T>>(),
                    It.IsAny<FindOptions<T, T>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NewCursor([.. items]));
        }

        private static MigrationTracker Tracker(string itemId = "tracker-1") => new()
        {
            ItemId = itemId,
            ProjectKey = "SOURCE-1",
            TargetedProjectKey = "TARGET-1",
            TenantGroupId = "group-1"
        };

        #region Tracker CRUD

        [Fact]
        public async Task CreateMigrationTrackerAsync_InsertsAndReturnsTheItemId()
        {
            var tracker = Tracker();

            var result = await CreateRepository().CreateMigrationTrackerAsync(tracker);

            result.Should().Be("tracker-1");
            _trackers.Verify(c => c.InsertOneAsync(tracker, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetMigrationTrackerAsync_ReturnsTheStoredTracker()
        {
            SetupFind(_trackers, Tracker());

            var result = await CreateRepository().GetMigrationTrackerAsync("tracker-1");

            result!.ItemId.Should().Be("tracker-1");
        }

        [Fact]
        public async Task GetMigrationTrackerAsync_NoMatch_ReturnsNull()
        {
            SetupFind(_trackers);

            var result = await CreateRepository().GetMigrationTrackerAsync("missing");

            result.Should().BeNull();
        }

        [Theory]
        [InlineData(MigrationServiceNames.Authentication)]
        [InlineData(MigrationServiceNames.IAM)]
        [InlineData(MigrationServiceNames.MFA)]
        [InlineData(MigrationServiceNames.CAPTCHA)]
        [InlineData(MigrationServiceNames.Email)]
        [InlineData(MigrationServiceNames.DataGateway)]
        [InlineData(MigrationServiceNames.Notifications)]
        [InlineData(MigrationServiceNames.Storage)]
        [InlineData(MigrationServiceNames.Language)]
        public async Task UpdateServiceStatusAsync_EveryKnownService_UpdatesTheTracker(MigrationServiceNames serviceName)
        {
            _trackers.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<MigrationTracker>>(), It.IsAny<UpdateDefinition<MigrationTracker>>(),
                    It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            var result = await CreateRepository().UpdateServiceStatusAsync("tracker-1", serviceName, true);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateServiceStatusAsync_NothingModified_ReturnsFalse()
        {
            _trackers.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<MigrationTracker>>(), It.IsAny<UpdateDefinition<MigrationTracker>>(),
                    It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 0, null));

            var result = await CreateRepository().UpdateServiceStatusAsync(
                "tracker-1", MigrationServiceNames.IAM, false, "failed");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateServiceStatusAsync_UnknownService_Throws()
        {
            var act = async () => await CreateRepository().UpdateServiceStatusAsync(
                "tracker-1", (MigrationServiceNames)99, true);

            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task GetMigrationsByProjectKeysAsync_KeepsOnlyTrackersWithIncompleteServices()
        {
            var incomplete = Tracker("tracker-1");
            incomplete.Email = new ServiceMigrationStatus { IsCompleted = false };
            var complete = Tracker("tracker-2");
            complete.Email = new ServiceMigrationStatus { IsCompleted = true };
            SetupFind(_trackers, incomplete, complete);

            var result = await CreateRepository().GetMigrationsByProjectKeysAsync(["SOURCE-1"]);

            result.Should().ContainSingle(t => t.ItemId == "tracker-1");
        }

        [Theory]
        [InlineData("Authentication")]
        [InlineData("IAM")]
        [InlineData("MFA")]
        [InlineData("CAPTCHA")]
        [InlineData("DataGateway")]
        [InlineData("Notifications")]
        [InlineData("Storage")]
        [InlineData("LanguageService")]
        public async Task GetMigrationsByTenantGroupIdAsync_AnyIncompleteServiceKeepsTheTracker(string serviceProperty)
        {
            var tracker = Tracker();
            typeof(MigrationTracker).GetProperty(serviceProperty)!
                .SetValue(tracker, new ServiceMigrationStatus { IsCompleted = false });
            SetupFind(_trackers, tracker);

            var result = await CreateRepository().GetMigrationsByTenantGroupIdAsync("group-1");

            result.Should().ContainSingle();
        }

        [Fact]
        public async Task GetMigrationsByTenantGroupIdAsync_NoTrackedServices_ReturnsNothing()
        {
            SetupFind(_trackers, Tracker());

            var result = await CreateRepository().GetMigrationsByTenantGroupIdAsync("group-1");

            result.Should().BeEmpty();
        }

        #endregion

        #region Collection migration

        [Fact]
        public async Task MigrateCollectionAsync_EmptySourceCollection_MigratesNothing()
        {
            _sourceCollection.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            var (totalDocuments, migratedDocuments) = await CreateRepository()
                .MigrateCollectionAsync("SOURCE-1", "TARGET-1", "EmailTemplates", true);

            totalDocuments.Should().Be(0);
            migratedDocuments.Should().Be(0);
            _targetCollection.Verify(c => c.BulkWriteAsync(
                It.IsAny<IEnumerable<WriteModel<BsonDocument>>>(), It.IsAny<BulkWriteOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MigrateCollectionAsync_OverwriteEnabled_UpsertsEveryDocument()
        {
            _sourceCollection.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(2);
            SetupFind(_sourceCollection,
                new BsonDocument { ["_id"] = "doc-1", ["Name"] = "one" },
                new BsonDocument { ["_id"] = "doc-2", ["Name"] = "two" });

            List<WriteModel<BsonDocument>>? written = null;
            _targetCollection.Setup(c => c.BulkWriteAsync(
                    It.IsAny<IEnumerable<WriteModel<BsonDocument>>>(), It.IsAny<BulkWriteOptions>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<WriteModel<BsonDocument>>, BulkWriteOptions, CancellationToken>((ops, _, _) => written = [.. ops])
                .ReturnsAsync(new BulkWriteResult<BsonDocument>.Acknowledged(2, 0, 0, 2, 0, [], []));

            var (totalDocuments, migratedDocuments) = await CreateRepository()
                .MigrateCollectionAsync("SOURCE-1", "TARGET-1", "EmailTemplates", true);

            totalDocuments.Should().Be(2);
            migratedDocuments.Should().Be(2);
            written.Should().HaveCount(2);
            written.Should().AllBeOfType<ReplaceOneModel<BsonDocument>>();
        }

        [Fact]
        public async Task MigrateCollectionAsync_OverwriteDisabled_UsesInsertOnlyUpdates()
        {
            _sourceCollection.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            SetupFind(_sourceCollection, new BsonDocument { ["_id"] = "doc-1", ["Name"] = "one", ["Language"] = "en-US" });

            List<WriteModel<BsonDocument>>? written = null;
            _targetCollection.Setup(c => c.BulkWriteAsync(
                    It.IsAny<IEnumerable<WriteModel<BsonDocument>>>(), It.IsAny<BulkWriteOptions>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<WriteModel<BsonDocument>>, BulkWriteOptions, CancellationToken>((ops, _, _) => written = [.. ops])
                .ReturnsAsync(new BulkWriteResult<BsonDocument>.Acknowledged(1, 0, 0, 0, 0, [], []));

            var (totalDocuments, migratedDocuments) = await CreateRepository()
                .MigrateCollectionAsync("SOURCE-1", "TARGET-1", "EmailTemplates", false);

            totalDocuments.Should().Be(1);
            migratedDocuments.Should().Be(0, "the acknowledgement reported no upserts");
            written.Should().ContainSingle().Which.Should().BeOfType<UpdateOneModel<BsonDocument>>();
        }

        #endregion

        #region Cleanup and document copy

        [Fact]
        public async Task CleanupCollectionAsync_AcknowledgedDelete_ReturnsTrue()
        {
            _targetCollection.Setup(c => c.DeleteManyAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteResult.Acknowledged(3));

            var result = await CreateRepository().CleanupCollectionAsync("TARGET-1", "BlocksLanguages");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task CleanupCollectionAsync_DeleteThrows_ReturnsFalse()
        {
            _targetCollection.Setup(c => c.DeleteManyAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mongo down"));

            var result = await CreateRepository().CleanupCollectionAsync("TARGET-1", "BlocksLanguages");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task MigrateDocumentsAsync_CopiesSeedDocumentsIntoTheTarget()
        {
            _sourceCollection.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            SetupFind(_sourceCollection, new BsonDocument { ["_id"] = "seed-1" });

            var result = await CreateRepository().MigrateDocumentsAsync("TARGET-1", "BlocksLanguages");

            result.Should().BeTrue();
            _targetCollection.Verify(c => c.InsertManyAsync(
                It.IsAny<IEnumerable<BsonDocument>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task MigrateDocumentsAsync_EmptySource_SkipsTheInsert()
        {
            _sourceCollection.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            var result = await CreateRepository().MigrateDocumentsAsync("TARGET-1", "BlocksLanguages");

            result.Should().BeTrue();
            _targetCollection.Verify(c => c.InsertManyAsync(
                It.IsAny<IEnumerable<BsonDocument>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MigrateDocumentsAsync_CopyThrows_ReturnsFalse()
        {
            _sourceCollection.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mongo down"));

            var result = await CreateRepository().MigrateDocumentsAsync("TARGET-1", "BlocksLanguages");

            result.Should().BeFalse();
        }

        #endregion
    }

    public class SubscriptionRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IMongoCollection<ResourceLimit>> _resourceLimits = new();

        public SubscriptionRepositoryTests()
        {
            _dbContextProvider.Setup(p => p.GetCollection<ResourceLimit>("ResourceLimits")).Returns(_resourceLimits.Object);
        }

        private static IAsyncCursor<T> NewCursor<T>(List<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(items.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(items.Count > 0).ReturnsAsync(false);
            return cursor.Object;
        }

        [Fact]
        public async Task GetSubscriptionsAsync_ReturnsEveryResourceLimit()
        {
            _resourceLimits.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<ResourceLimit>>(),
                    It.IsAny<FindOptions<ResourceLimit, ResourceLimit>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NewCursor(new List<ResourceLimit>
                {
                    new() { Resource = "people::invite", Limit = 5 },
                    new() { Resource = "storage::files", Limit = 100 }
                }));

            var result = await new SubscriptionRepository(_dbContextProvider.Object).GetSubscriptionsAsync();

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetSubscriptionsAsync_NoLimits_ReturnsEmptyList()
        {
            _resourceLimits.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<ResourceLimit>>(),
                    It.IsAny<FindOptions<ResourceLimit, ResourceLimit>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NewCursor(new List<ResourceLimit>()));

            var result = await new SubscriptionRepository(_dbContextProvider.Object).GetSubscriptionsAsync();

            result.Should().BeEmpty();
        }
    }
}
