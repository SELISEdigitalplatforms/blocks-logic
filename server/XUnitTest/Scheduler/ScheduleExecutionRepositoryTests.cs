using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using DataAccess.InternalService;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Scheduler.DomainService.Repositories;

namespace XUnitTest.Scheduler
{
    /// <summary>
    /// <see cref="ScheduleExecutionRepository"/>: the filters, sort, paging, hints, projection and indexes it sends,
    /// rendered to BSON (no Mongo server).
    /// </summary>
    public class ScheduleExecutionRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<ILogicDataAccess> _dataAccess = new();
        private readonly Mock<IMongoCollection<ScheduleExecution>> _collection = new();
        private readonly Mock<IMongoIndexManager<ScheduleExecution>> _indexes = new();

        private FindOptions<ScheduleExecution, ScheduleExecution>? _findOptions;
        private FilterDefinition<ScheduleExecution>? _findFilter;
        private CountOptions? _countOptions;
        private FilterDefinition<ScheduleExecution>? _countFilter;

        public ScheduleExecutionRepositoryTests()
        {
            _dbContextProvider.Setup(p => p.GetCollection<ScheduleExecution>("ScheduleExecutions")).Returns(_collection.Object);
            _dbContextProvider.Setup(p => p.GetCollection<ScheduleExecution>("t1", "ScheduleExecutions")).Returns(_collection.Object);
            _collection.Setup(c => c.Indexes).Returns(_indexes.Object);
            GrantReads();
            _dataAccess.Setup(d => d.InsertAllowedAsync(LogicSchemas.ScheduleExecution, It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(true);
            _dataAccess.Setup(d => d.PrepareWriteAsync(LogicSchemas.ScheduleExecution, It.IsAny<ScheduleExecution>(), true, It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync((string _, ScheduleExecution e, bool _, string _, string? _) => e);

            _collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<ScheduleExecution>>(), It.IsAny<FindOptions<ScheduleExecution, ScheduleExecution>>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<ScheduleExecution>, FindOptions<ScheduleExecution, ScheduleExecution>, CancellationToken>((f, o, _) => { _findFilter = f; _findOptions = o; })
                .ReturnsAsync(() => NewCursor(new List<ScheduleExecution> { new() { ItemId = "e1", ScheduleId = "s1" } }));
            _collection.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<ScheduleExecution>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<ScheduleExecution>, CountOptions, CancellationToken>((f, o, _) => { _countFilter = f; _countOptions = o; })
                .ReturnsAsync(42);
        }

        private void GrantReads(bool granted = true) =>
            _dataAccess.Setup(d => d.ReadAsync(LogicSchemas.ScheduleExecution, It.IsAny<FilterDefinition<ScheduleExecution>>(), It.IsAny<string>(), null, It.IsAny<bool>()))
                .ReturnsAsync((string _, FilterDefinition<ScheduleExecution> f, string _, string? _, bool _) =>
                    new ReadScope<ScheduleExecution>(granted, f, null));

        private ScheduleExecutionRepository Create() => new(_dbContextProvider.Object, _dataAccess.Object);

        private static IAsyncCursor<T> NewCursor<T>(List<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(items.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(items.Count > 0).ReturnsAsync(false);
            return cursor.Object;
        }

        private static RenderArgs<ScheduleExecution> Args =>
            new(BsonSerializer.LookupSerializer<ScheduleExecution>(), BsonSerializer.SerializerRegistry);

        private static BsonDocument Render(FilterDefinition<ScheduleExecution> filter) => filter.Render(Args);

        private static BsonDocument Doc(string json) => BsonDocument.Parse(json);

        private static readonly DateTime Start = new(2026, 10, 19, 18, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime End = new(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);

        // ---------- Filters ----------

        [Fact]
        public void Filter_All_IsScheduleIdOnly_NoTenantId()
        {
            Render(ScheduleExecutionRepository.BuildFilter(new ScheduleExecutionFilter("s1")))
                .ShouldEqual(Doc("{ ScheduleId: 's1' }"));
        }

        [Fact]
        public void Filter_Statuses_IsAnInEqualityList()
        {
            var rendered = Render(ScheduleExecutionRepository.BuildFilter(new ScheduleExecutionFilter("s1", [500, 502])));

            rendered.ShouldEqual(Doc("{ ScheduleId: 's1', 'Response.Status': { $in: [500, 502] } }"));
            rendered.Contains("TenantId").Should().BeFalse();
        }

        [Fact]
        public void Filter_NonSuccessRange_IsTheFallbackRange()
        {
            Render(ScheduleExecutionRepository.BuildFilter(new ScheduleExecutionFilter("s1", NonSuccessRange: true)))
                .ShouldEqual(Doc("{ ScheduleId: 's1', $or: [ { 'Response.Status': { $lt: 200 } }, { 'Response.Status': { $gt: 299 } } ] }"));
        }

        [Fact]
        public void Filter_DateRange_StartInclusive_EndExclusive()
        {
            Render(ScheduleExecutionRepository.BuildFilter(new ScheduleExecutionFilter("s1", [200], Start, End)))
                .ShouldEqual(new BsonDocument
                {
                    { "ScheduleId", "s1" },
                    { "Response.Status", new BsonDocument("$in", new BsonArray { 200 }) },
                    { "FiredUtc", new BsonDocument { { "$gte", Start }, { "$lt", End } } },
                });
        }

        [Fact]
        public void Filter_EachDateBoundAlone()
        {
            Render(ScheduleExecutionRepository.BuildFilter(new ScheduleExecutionFilter("s1", StartUtc: Start)))
                .ShouldEqual(new BsonDocument { { "ScheduleId", "s1" }, { "FiredUtc", new BsonDocument("$gte", Start) } });
            Render(ScheduleExecutionRepository.BuildFilter(new ScheduleExecutionFilter("s1", EndUtc: End)))
                .ShouldEqual(new BsonDocument { { "ScheduleId", "s1" }, { "FiredUtc", new BsonDocument("$lt", End) } });
        }

        [Fact]
        public void Filter_ById_IsScheduleAndId()
        {
            Render(ScheduleExecutionRepository.ByIdFilter("s1", "e1")).ShouldEqual(Doc("{ ScheduleId: 's1', _id: 'e1' }"));
        }

        // ---------- Sort, projection, hints, indexes ----------

        [Fact]
        public void Sort_IsFiredDescThenIdDesc()
        {
            ScheduleExecutionRepository.NewestFirst.Render(Args).ShouldEqual(Doc("{ FiredUtc: -1, _id: -1 }"));
        }

        [Fact]
        public void Projection_IsTheLightListColumns()
        {
            ScheduleExecutionRepository.ListProjection.Render(Args)
                .ShouldEqual(Doc("{ _id: 1, FiredUtc: 1, FinishedUtc: 1, 'Response.Status': 1 }"));
        }

        [Fact]
        public void Hint_FollowsTheOutcomeFilter()
        {
            ScheduleExecutionRepository.HintFor(new ScheduleExecutionFilter("s1")).Should().Be(new BsonString("ix_schedexec_schedule_fired"));
            ScheduleExecutionRepository.HintFor(new ScheduleExecutionFilter("s1", StartUtc: Start)).Should().Be(new BsonString("ix_schedexec_schedule_fired"));
            ScheduleExecutionRepository.HintFor(new ScheduleExecutionFilter("s1", [200])).Should().Be(new BsonString("ix_schedexec_schedule_status_fired"));
            ScheduleExecutionRepository.HintFor(new ScheduleExecutionFilter("s1", NonSuccessRange: true)).Should().Be(new BsonString("ix_schedexec_schedule_status_fired"));
        }

        [Fact]
        public void Indexes_AreTheTwoNamedOnes_NoTtl()
        {
            var models = ScheduleExecutionRepository.IndexModels();

            models.Select(m => m.Options.Name).Should().Equal("ix_schedexec_schedule_fired", "ix_schedexec_schedule_status_fired");
            models[0].Keys.Render(Args).ShouldEqual(Doc("{ ScheduleId: 1, FiredUtc: -1, _id: -1 }"));
            models[1].Keys.Render(Args).ShouldEqual(Doc("{ ScheduleId: 1, 'Response.Status': 1, FiredUtc: -1, _id: -1 }"));
            models.Should().OnlyContain(m => m.Options.ExpireAfter == null);
        }

        [Fact]
        public void StatusField_IsResponseStatus()
        {
            ScheduleExecutionRepository.StatusField.Render(Args).FieldName.Should().Be("Response.Status");
        }

        // ---------- Calls ----------

        [Fact]
        public async Task GetPageAsync_SkipsAndLimits_WithSortProjectionAndHint()
        {
            var filter = new ScheduleExecutionFilter("s1", [500]);

            var rows = await Create().GetPageAsync(filter, pageNumber: 3, pageSize: 50);

            rows.Should().ContainSingle();
            _findOptions!.Skip.Should().Be(150);
            _findOptions.Limit.Should().Be(50);
            _findOptions.Hint.Should().Be(new BsonString("ix_schedexec_schedule_status_fired"));
            _findOptions.Sort.Render(Args).ShouldEqual(Doc("{ FiredUtc: -1, _id: -1 }"));
            _findOptions.Projection.Render(Args).Document.ShouldEqual(Doc("{ _id: 1, FiredUtc: 1, FinishedUtc: 1, 'Response.Status': 1 }"));
            Render(_findFilter!).ShouldEqual(Doc("{ ScheduleId: 's1', 'Response.Status': { $in: [500] } }"));
        }

        [Fact]
        public async Task CountAsync_UsesTheSameFilterAndHint()
        {
            var filter = new ScheduleExecutionFilter("s1", StartUtc: Start, EndUtc: End);

            var total = await Create().CountAsync(filter);
            await Create().GetPageAsync(filter, 0, 10);

            total.Should().Be(42);
            Render(_countFilter!).ShouldEqual(Render(_findFilter!));
            _countOptions!.Hint.Should().Be(_findOptions!.Hint).And.Be(new BsonString("ix_schedexec_schedule_fired"));
        }

        [Fact]
        public async Task Reads_NotGranted_ReturnEmpty_AndQueryNothing()
        {
            GrantReads(granted: false);
            var repository = Create();

            (await repository.GetPageAsync(new ScheduleExecutionFilter("s1"), 0, 10)).Should().BeEmpty();
            (await repository.CountAsync(new ScheduleExecutionFilter("s1"))).Should().Be(0);
            (await repository.GetByIdAsync("s1", "e1")).Should().BeNull();
            (await repository.GetDistinctStatusesAsync("s1")).Should().BeEmpty();

            _collection.Verify(c => c.FindAsync(It.IsAny<FilterDefinition<ScheduleExecution>>(), It.IsAny<FindOptions<ScheduleExecution, ScheduleExecution>>(), It.IsAny<CancellationToken>()), Times.Never);
            _collection.Verify(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<ScheduleExecution>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetDistinctStatusesAsync_KeepsIntegerValuesOnly()
        {
            _collection.Setup(c => c.DistinctAsync(It.IsAny<FieldDefinition<ScheduleExecution, BsonValue>>(), It.IsAny<FilterDefinition<ScheduleExecution>>(), It.IsAny<DistinctOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(NewCursor(new List<BsonValue> { 200, new BsonInt64(500), "oops", BsonNull.Value, 504, 2.5 }));

            var statuses = await Create().GetDistinctStatusesAsync("s1");

            statuses.Should().BeEquivalentTo([200, 500, 504]);
        }

        [Fact]
        public async Task InsertAsync_CreatesIndexesOncePerTenant_ThenInserts()
        {
            var repository = Create();
            var row = new ScheduleExecution { ItemId = "e1", ScheduleId = "s1", TenantId = "t1" };

            await repository.InsertAsync(row);
            await repository.InsertAsync(row);

            _indexes.Verify(i => i.CreateManyAsync(It.IsAny<IEnumerable<CreateIndexModel<ScheduleExecution>>>(), It.IsAny<CancellationToken>()), Times.Once);
            _collection.Verify(c => c.InsertOneAsync(row, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task InsertAsync_IndexCreationFails_StillInserts_AndRetriesLater()
        {
            _indexes.Setup(i => i.CreateManyAsync(It.IsAny<IEnumerable<CreateIndexModel<ScheduleExecution>>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new MongoCommandException(new MongoDB.Driver.Core.Connections.ConnectionId(new MongoDB.Driver.Core.Servers.ServerId(
                    new MongoDB.Driver.Core.Clusters.ClusterId(), new System.Net.DnsEndPoint("localhost", 27017))), "index conflict", new BsonDocument()));
            var repository = Create();
            var row = new ScheduleExecution { ItemId = "e1", ScheduleId = "s1", TenantId = "t1" };

            await repository.InsertAsync(row);
            await repository.InsertAsync(row);

            _indexes.Verify(i => i.CreateManyAsync(It.IsAny<IEnumerable<CreateIndexModel<ScheduleExecution>>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            _collection.Verify(c => c.InsertOneAsync(row, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task InsertAsync_WithoutTenant_Throws()
        {
            await Create().Invoking(r => r.InsertAsync(new ScheduleExecution { ItemId = "e1", ScheduleId = "s1" }))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    internal static class BsonDocumentAssertions
    {
        /// <summary>Exact, order-sensitive comparison of two rendered documents.</summary>
        public static void ShouldEqual(this BsonDocument actual, BsonDocument expected) =>
            actual.ToJson().Should().Be(expected.ToJson());
    }
}
