using Blocks.Genesis;
using Cloud.LmtService.Models.Logs;
using Cloud.LmtService.Repositories.Logs;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Cloud
{
    /// <summary>
    /// Unit tests for <see cref="LogRepository"/>. Every read is scoped to the calling tenant before
    /// anything else is applied, which is the property that keeps one tenant's logs out of another's
    /// console. The rest of each filter is assembled from optional query fields, so these pin which
    /// clauses appear for which inputs, and which collection the read lands in.
    /// </summary>
    public class LogRepositoryTests : IDisposable
    {
        private readonly Mock<IDbContextProvider> _provider = new();
        private readonly Mock<IMongoDatabase> _database = new();
        private readonly Mock<IMongoCollection<BsonDocument>> _logs = new();
        private readonly LogRepository _sut;

        private FilterDefinition<BsonDocument>? _lastFilter;
        private string? _lastCollectionName;

        public LogRepositoryTests()
        {
            BlocksContext.IsTestMode = true;
            SetTenant("tenant-1");

            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.LogConnectionString).Returns("mongodb://logs");
            secret.SetupGet(s => s.LogDatabaseName).Returns("logsdb");

            _provider.Setup(p => p.GetDatabase("mongodb://logs", "logsdb")).Returns(_database.Object);
            _database.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null))
                     .Callback<string, MongoCollectionSettings>((name, _) => _lastCollectionName = name)
                     .Returns(_logs.Object);

            SetupProjectedFind();
            _logs.Setup(c => c.CountDocumentsAsync(
                     It.IsAny<FilterDefinition<BsonDocument>>(),
                     It.IsAny<CountOptions>(),
                     It.IsAny<CancellationToken>()))
                 .ReturnsAsync(0);

            _sut = new LogRepository(
                secret.Object, _provider.Object,
                new Mock<ILogger<LogRepository>>().Object,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetTenant(string? tenantId) =>
            BlocksContext.SetContext(tenantId is null
                ? null
                : BlocksContext.Create(tenantId, null, "user-1", true, null, null,
                    DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", tenantId));

        /// <summary>The reads project to LogProjection, so the cursor is of that type.</summary>
        private void SetupProjectedFind(params LogProjection[] documents)
        {
            var cursor = new Mock<IAsyncCursor<LogProjection>>();
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(documents.Length > 0)
                  .ReturnsAsync(false);
            cursor.SetupGet(c => c.Current).Returns(documents);

            _logs.Setup(c => c.FindAsync(
                     It.IsAny<FilterDefinition<BsonDocument>>(),
                     It.IsAny<FindOptions<BsonDocument, LogProjection>>(),
                     It.IsAny<CancellationToken>()))
                 .Callback<FilterDefinition<BsonDocument>, FindOptions<BsonDocument, LogProjection>, CancellationToken>(
                     (f, _, _) => _lastFilter = f)
                 .ReturnsAsync(cursor.Object);
        }

        private string RenderedFilter() =>
            _lastFilter!.Render(new RenderArgs<BsonDocument>(
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(),
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString();

        // ---- the live tail ----

        [Fact]
        public async Task GetLogs_Live_ReadsTheNamedServiceCollection()
        {
            await _sut.GetLogs(new LiveLogRequest { Name = "blocks-logic", LastDate = DateTime.UtcNow.AddMinutes(-1) });

            _lastCollectionName.Should().Be("blocks-logic");
        }

        [Fact]
        public async Task GetLogs_Live_ScopesToTheCallingTenant()
        {
            await _sut.GetLogs(new LiveLogRequest { Name = "svc", LastDate = DateTime.UtcNow });

            RenderedFilter().Should().Contain("TenantId").And.Contain("tenant-1");
        }

        [Fact]
        public async Task GetLogs_Live_OnlyReturnsEntriesNewerThanTheCursor()
        {
            await _sut.GetLogs(new LiveLogRequest { Name = "svc", LastDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc) });

            RenderedFilter().Should().Contain("Timestamp").And.Contain("$gt");
        }

        [Fact]
        public async Task GetLogs_Live_ReturnsWhatTheProjectionYields()
        {
            SetupProjectedFind(new LogProjection { Message = "hello" }, new LogProjection { Message = "world" });

            var result = await _sut.GetLogs(new LiveLogRequest { Name = "svc", LastDate = DateTime.UtcNow });

            result.Should().HaveCount(2);
        }

        // ---- the paged search ----

        [Fact]
        public async Task GetLogs_Paged_ScopesToTheCallingTenant()
        {
            await _sut.GetLogs(new GetLogsRequest { ServiceName = "svc", Page = 0, PageSize = 25 });

            RenderedFilter().Should().Contain("tenant-1");
        }

        [Fact]
        public async Task GetLogs_Paged_AppliesNoOptionalClauseWhenNoneIsGiven()
        {
            await _sut.GetLogs(new GetLogsRequest { ServiceName = "svc", Page = 0, PageSize = 25 });

            var rendered = RenderedFilter();
            rendered.Should().NotContain("TraceId");
            rendered.Should().NotContain("SpanId");
            rendered.Should().NotContain("Level");
        }

        [Fact]
        public async Task GetLogs_Paged_SearchesTheMessageCaseInsensitively()
        {
            await _sut.GetLogs(new GetLogsRequest
            {
                ServiceName = "svc", Page = 0, PageSize = 25, Search = "timeout",
            });

            RenderedFilter().Should().Contain("Message").And.Contain("timeout").And.Contain("\"i\"");
        }

        [Fact]
        public async Task GetLogs_Paged_FiltersByTraceId()
        {
            await _sut.GetLogs(new GetLogsRequest
            {
                ServiceName = "svc", Page = 0, PageSize = 25,
                Filter = new GetLogsRequestFilter { TraceId = "trace-1" },
            });

            RenderedFilter().Should().Contain("TraceId").And.Contain("trace-1");
        }

        [Fact]
        public async Task GetLogs_Paged_FiltersBySpanId()
        {
            await _sut.GetLogs(new GetLogsRequest
            {
                ServiceName = "svc", Page = 0, PageSize = 25,
                Filter = new GetLogsRequestFilter { SpanId = "span-1" },
            });

            RenderedFilter().Should().Contain("SpanId").And.Contain("span-1");
        }

        [Fact]
        public async Task GetLogs_Paged_FiltersByLevel()
        {
            await _sut.GetLogs(new GetLogsRequest
            {
                ServiceName = "svc", Page = 0, PageSize = 25,
                Filter = new GetLogsRequestFilter { Level = "Error" },
            });

            RenderedFilter().Should().Contain("Level").And.Contain("Error");
        }

        [Fact]
        public async Task GetLogs_Paged_BoundsTheWindowAtBothEnds()
        {
            await _sut.GetLogs(new GetLogsRequest
            {
                ServiceName = "svc", Page = 0, PageSize = 25,
                Filter = new GetLogsRequestFilter
                {
                    StartDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
                },
            });

            var rendered = RenderedFilter();
            rendered.Should().Contain("$gt");
            rendered.Should().Contain("$lte");
        }

        [Fact]
        public async Task GetLogs_Paged_CombinesEveryClauseGiven()
        {
            await _sut.GetLogs(new GetLogsRequest
            {
                ServiceName = "svc", Page = 0, PageSize = 25, Search = "boom",
                Filter = new GetLogsRequestFilter { TraceId = "t", SpanId = "s", Level = "Error" },
            });

            var rendered = RenderedFilter();
            rendered.Should().Contain("TenantId");
            rendered.Should().Contain("Message");
            rendered.Should().Contain("TraceId");
            rendered.Should().Contain("SpanId");
            rendered.Should().Contain("Level");
        }

        [Fact]
        public async Task GetLogs_Paged_ReportsTheTotalAlongsideThePage()
        {
            SetupProjectedFind(new LogProjection { Message = "one" });
            _logs.Setup(c => c.CountDocumentsAsync(
                     It.IsAny<FilterDefinition<BsonDocument>>(),
                     It.IsAny<CountOptions>(),
                     It.IsAny<CancellationToken>()))
                 .ReturnsAsync(412);

            var (items, count) = await _sut.GetLogs(new GetLogsRequest
            {
                ServiceName = "svc", Page = 0, PageSize = 25,
            });

            items.Should().ContainSingle();
            count.Should().Be(412);
        }

        // ---- the date-range read ----

        [Fact]
        public async Task GetLogs_ByDate_ScopesToTheCallingTenant()
        {
            await _sut.GetLogs(new LogsByDateRequest { ServiceName = "svc" });

            RenderedFilter().Should().Contain("tenant-1");
        }

        [Fact]
        public async Task GetLogs_ByDate_UsesAHalfOpenWindow()
        {
            await _sut.GetLogs(new LogsByDateRequest
            {
                ServiceName = "svc",
                Filter = new LogsByLastDateRequestFilter
                {
                    StartDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
                },
            });

            // Inclusive start, exclusive end, so adjacent days cannot double-count an entry.
            var rendered = RenderedFilter();
            rendered.Should().Contain("$gte");
            rendered.Should().Contain("$lt\"");
        }

        [Fact]
        public async Task GetLogs_ByDate_SearchesTheMessage()
        {
            await _sut.GetLogs(new LogsByDateRequest { ServiceName = "svc", Search = "failure" });

            RenderedFilter().Should().Contain("Message").And.Contain("failure");
        }

        [Fact]
        public async Task GetLogs_ByDate_ReadsTheNamedServiceCollection()
        {
            await _sut.GetLogs(new LogsByDateRequest { ServiceName = "blocks-iam" });

            _lastCollectionName.Should().Be("blocks-iam");
        }

        // ---- no ambient tenant ----

        [Fact]
        public async Task GetLogs_WithoutATenantContextStillFiltersOnTenantId()
        {
            // A null tenant must narrow to nothing rather than widen to everything.
            SetTenant(null);

            await _sut.GetLogs(new GetLogsRequest { ServiceName = "svc", Page = 0, PageSize = 25 });

            RenderedFilter().Should().Contain("TenantId");
        }
    }
}
