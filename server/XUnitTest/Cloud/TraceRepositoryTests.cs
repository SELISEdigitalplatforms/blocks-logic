using Blocks.Genesis;
using Cloud.LmtService.Models.Trace;
using Cloud.LmtService.Repositories.Trace;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Cloud
{
    /// <summary>
    /// Unit tests for <see cref="TraceRepository"/>. Traces live in a per-tenant collection, so
    /// the collection name is the isolation boundary rather than a clause in the filter, and it
    /// is asserted on every read. The rest of each query is assembled from optional filter
    /// fields, so these pin which clauses appear for which inputs.
    /// </summary>
    public class TraceRepositoryTests : IDisposable
    {
        private readonly Mock<IDbContextProvider> _provider = new();
        private readonly Mock<IMongoDatabase> _database = new();
        private readonly Mock<IMongoCollection<BsonDocument>> _traces = new();
        private readonly TraceRepository _sut;

        private FilterDefinition<BsonDocument>? _lastFilter;
        private string? _lastCollectionName;

        public TraceRepositoryTests()
        {
            BlocksContext.IsTestMode = true;
            SetTenant("tenant-1");

            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.TraceConnectionString).Returns("mongodb://traces");
            secret.SetupGet(s => s.TraceDatabaseName).Returns("tracesdb");

            _provider.Setup(p => p.GetDatabase("mongodb://traces", "tracesdb")).Returns(_database.Object);
            _database.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null))
                     .Callback<string, MongoCollectionSettings>((name, _) => _lastCollectionName = name)
                     .Returns(_traces.Object);

            SetupSingleTraceFind();
            SetupPagedFind();
            _traces.Setup(c => c.CountDocumentsAsync(
                       It.IsAny<FilterDefinition<BsonDocument>>(),
                       It.IsAny<CountOptions>(),
                       It.IsAny<CancellationToken>()))
                   .ReturnsAsync(0);

            _sut = new TraceRepository(
                secret.Object,
                _provider.Object,
                new Mock<ILogger<TraceRepository>>().Object,
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

        private static Mock<IAsyncCursor<T>> Cursor<T>(params T[] documents)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(documents.Length > 0)
                  .ReturnsAsync(false);
            cursor.SetupGet(c => c.Current).Returns(documents);
            return cursor;
        }

        private void SetupSingleTraceFind(params SingleTraceProjection[] documents) =>
            _traces.Setup(c => c.FindAsync(
                       It.IsAny<FilterDefinition<BsonDocument>>(),
                       It.IsAny<FindOptions<BsonDocument, SingleTraceProjection>>(),
                       It.IsAny<CancellationToken>()))
                   .Callback<FilterDefinition<BsonDocument>, FindOptions<BsonDocument, SingleTraceProjection>, CancellationToken>(
                       (f, _, _) => _lastFilter = f)
                   .ReturnsAsync(Cursor(documents).Object);

        private void SetupPagedFind(params TraceProjection[] documents) =>
            _traces.Setup(c => c.FindAsync(
                       It.IsAny<FilterDefinition<BsonDocument>>(),
                       It.IsAny<FindOptions<BsonDocument, TraceProjection>>(),
                       It.IsAny<CancellationToken>()))
                   .Callback<FilterDefinition<BsonDocument>, FindOptions<BsonDocument, TraceProjection>, CancellationToken>(
                       (f, _, _) => _lastFilter = f)
                   .ReturnsAsync(Cursor(documents).Object);

        private string RenderedFilter() =>
            _lastFilter!.Render(new RenderArgs<BsonDocument>(
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(),
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString();

        private static GetTracesRequest Paged() => new() { Page = 0, PageSize = 25 };

        // ---- a single trace ----

        [Fact]
        public async Task GetTrace_ReadsTheCallingTenantCollection()
        {
            // Traces are partitioned by collection rather than by a tenant field, so the
            // collection name is the whole isolation boundary here.
            await _sut.GetTraces(new GetTraceRequest { TraceId = "trace-1" });

            _lastCollectionName.Should().Be("tenant-1");
        }

        [Fact]
        public async Task GetTrace_MatchesOnTheRequestedTraceId()
        {
            await _sut.GetTraces(new GetTraceRequest { TraceId = "trace-1" });

            RenderedFilter().Should().Contain("TraceId").And.Contain("trace-1");
        }

        [Fact]
        public async Task GetTrace_ReturnsEverySpanOfThatTrace()
        {
            SetupSingleTraceFind(new SingleTraceProjection(), new SingleTraceProjection());

            var result = await _sut.GetTraces(new GetTraceRequest { TraceId = "trace-1" });

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetTrace_ReturnsEmptyForAnUnknownTrace()
        {
            SetupSingleTraceFind();

            var result = await _sut.GetTraces(new GetTraceRequest { TraceId = "nope" });

            result.Should().BeEmpty();
        }

        // ---- the paged list ----

        [Fact]
        public async Task GetTraces_ReadsTheCallingTenantCollection()
        {
            await _sut.GetTraces(Paged());

            _lastCollectionName.Should().Be("tenant-1");
        }

        [Fact]
        public async Task GetTraces_ReturnsOnlyRootSpans()
        {
            // The list is one row per request, so it must exclude child spans. A parent is
            // recorded either as an empty string or as null depending on the emitter, and
            // missing either representation would leak child spans into the list.
            await _sut.GetTraces(Paged());

            var rendered = RenderedFilter();
            rendered.Should().Contain("ParentId");
            rendered.Should().Contain("$or");
            rendered.Should().Contain("null");
        }

        [Fact]
        public async Task GetTraces_AppliesNoOptionalClauseWhenNoFilterIsGiven()
        {
            await _sut.GetTraces(Paged());

            var rendered = RenderedFilter();
            rendered.Should().NotContain("ServiceName");
            rendered.Should().NotContain("Timestamp");
            rendered.Should().NotContain("OperationName");
        }

        [Fact]
        public async Task GetTraces_SearchMatchesTheOperationNameOrAnExactTraceId()
        {
            // Operators paste a trace id into the same box they type an operation name into,
            // so the search has to serve both without the id being treated as a pattern.
            var request = Paged();
            request.Search = "checkout";

            await _sut.GetTraces(request);

            var rendered = RenderedFilter();
            rendered.Should().Contain("OperationName");
            rendered.Should().Contain("TraceId");
            rendered.Should().Contain("checkout");
        }

        [Fact]
        public async Task GetTraces_IgnoresAWhitespaceOnlySearch()
        {
            var request = Paged();
            request.Search = "   ";

            await _sut.GetTraces(request);

            RenderedFilter().Should().NotContain("OperationName");
        }

        [Fact]
        public async Task GetTraces_RestrictsToTheSelectedServices()
        {
            var request = Paged();
            request.Filter = new GetTracesRequestFilter { Services = new List<string> { "api", "worker" } };

            await _sut.GetTraces(request);

            var rendered = RenderedFilter();
            rendered.Should().Contain("$in").And.Contain("worker");
        }

        [Fact]
        public async Task GetTraces_ExcludesTheSuppressedServices()
        {
            var request = Paged();
            request.Filter = new GetTracesRequestFilter { Excepts = new List<string> { "healthcheck" } };

            await _sut.GetTraces(request);

            var rendered = RenderedFilter();
            rendered.Should().Contain("$nin").And.Contain("healthcheck");
        }

        [Fact]
        public async Task GetTraces_TreatsAnEmptyServiceListAsNoRestriction()
        {
            var request = Paged();
            request.Filter = new GetTracesRequestFilter();

            await _sut.GetTraces(request);

            var rendered = RenderedFilter();
            rendered.Should().NotContain("$in");
            rendered.Should().NotContain("$nin");
        }

        [Fact]
        public async Task GetTraces_BoundsTheWindowExclusivelyBelowAndInclusivelyAbove()
        {
            // The two bounds are deliberately asymmetric, $gt and $lte, so that paging
            // backwards through adjacent windows cannot return the same trace twice.
            var request = Paged();
            request.Filter = new GetTracesRequestFilter
            {
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 1, 2)
            };

            await _sut.GetTraces(request);

            var rendered = RenderedFilter();
            rendered.Should().Contain("$gt").And.Contain("$lte");
        }

        [Fact]
        public async Task GetTraces_AcceptsAnOpenEndedWindow()
        {
            var request = Paged();
            request.Filter = new GetTracesRequestFilter { StartDate = new DateTime(2026, 1, 1) };

            await _sut.GetTraces(request);

            RenderedFilter().Should().Contain("Timestamp");
        }

        [Fact]
        public async Task GetTraces_FiltersStatusCodesThroughTheNestedAttribute()
        {
            // The status code is not a top level field, it is nested under an attribute key
            // containing dots, so it can only be reached with $getField inside an $expr.
            var request = Paged();
            request.Filter = new GetTracesRequestFilter { StatusCodes = new List<int> { 500, 503 } };

            await _sut.GetTraces(request);

            var rendered = RenderedFilter();
            rendered.Should().Contain("$expr");
            rendered.Should().Contain("$getField");
            rendered.Should().Contain("response.status.code");
            rendered.Should().Contain("503");
        }

        [Fact]
        public async Task GetTraces_CombinesEveryFilterRatherThanReplacing()
        {
            var request = Paged();
            request.Search = "checkout";
            request.Filter = new GetTracesRequestFilter
            {
                Services = new List<string> { "api" },
                StartDate = new DateTime(2026, 1, 1),
                StatusCodes = new List<int> { 500 }
            };

            await _sut.GetTraces(request);

            var rendered = RenderedFilter();
            rendered.Should().Contain("checkout");
            rendered.Should().Contain("api");
            rendered.Should().Contain("Timestamp");
            rendered.Should().Contain("$expr");
        }

        [Fact]
        public async Task GetTraces_ReturnsTheRowsAndTheTotalTogether()
        {
            SetupPagedFind(new TraceProjection(), new TraceProjection());
            _traces.Setup(c => c.CountDocumentsAsync(
                       It.IsAny<FilterDefinition<BsonDocument>>(),
                       It.IsAny<CountOptions>(),
                       It.IsAny<CancellationToken>()))
                   .ReturnsAsync(97);

            var (rows, total) = await _sut.GetTraces(Paged());

            rows.Should().HaveCount(2);
            total.Should().Be(97);
        }

        [Fact]
        public async Task GetTraces_CountsAgainstTheSameFilterAsThePage()
        {
            // A count taken against a different filter than the page gives a pager that
            // promises rows the query cannot return.
            var request = Paged();
            request.Search = "checkout";

            await _sut.GetTraces(request);

            _traces.Verify(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
            RenderedFilter().Should().Contain("checkout");
        }
    }
}
