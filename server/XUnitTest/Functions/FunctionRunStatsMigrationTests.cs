using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Entities;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The counters' move from the retired <c>FunctionRunStats</c> collection onto the function
    /// document, against a real MongoDB — the guarantees are Mongo's (<c>$inc</c>, <c>$max</c>,
    /// find-and-delete), so a mock would only repeat the test's assumptions. Needs
    /// <c>BLOCKS_FUNCTIONS_TEST_MONGO</c> (default <c>mongodb://localhost:27017</c>); each run
    /// uses and drops its own database.
    /// </summary>
    public sealed class FunctionRunStatsMigrationTests : IDisposable
    {
        private const string Tenant = "tenant-stats";

        private readonly MongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly string _databaseName = "blocks_fn_stats_" + Guid.NewGuid().ToString("N");
        private readonly Mock<IDbContextProvider> _provider = new();
        private readonly FunctionRepository _functions;

        public FunctionRunStatsMigrationTests()
        {
            var url = Environment.GetEnvironmentVariable("BLOCKS_FUNCTIONS_TEST_MONGO") ?? "mongodb://localhost:27017";
            _client = new MongoClient(url);
            _database = _client.GetDatabase(_databaseName);

            _provider.Setup(p => p.GetCollection<FunctionEntity>(It.IsAny<string>(), FunctionsConstants.FunctionsCollection))
                .Returns(_database.GetCollection<FunctionEntity>(FunctionsConstants.FunctionsCollection));
            _provider.Setup(p => p.GetCollection<FunctionRunStatsMigration.LegacyRunStats>(It.IsAny<string>(), FunctionsConstants.LegacyRunStatsCollection))
                .Returns(Legacy);

            _functions = new FunctionRepository(_provider.Object, NullLogger<FunctionRepository>.Instance);
        }

        public void Dispose() => _client.DropDatabase(_databaseName);

        private IMongoCollection<FunctionRunStatsMigration.LegacyRunStats> Legacy =>
            _database.GetCollection<FunctionRunStatsMigration.LegacyRunStats>(FunctionsConstants.LegacyRunStatsCollection);

        private FunctionRunStatsMigration Migration(IFunctionRepository? functions = null) => new(
            _provider.Object, functions ?? _functions, Mock.Of<IFunctionTenantSource>(),
            Mock.Of<ICacheClient>(c => c.CacheDatabase() == Mock.Of<StackExchange.Redis.IDatabase>()),
            NullLogger<FunctionRunStatsMigration>.Instance);

        private static readonly DateTime Updated = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        private async Task<FunctionEntity> Function(string id, long totalRuns = 0, DateTime? lastRunAt = null)
        {
            var function = new FunctionEntity
            {
                ItemId = id, Name = id, LastUpdatedDate = Updated, TotalRuns = totalRuns, LastRunAt = lastRunAt,
            };
            await _functions.CreateAsync(Tenant, function);
            return function;
        }

        private Task<FunctionEntity> Read(string id) =>
            _database.GetCollection<FunctionEntity>(FunctionsConstants.FunctionsCollection)
                .Find(f => f.ItemId == id).FirstAsync();

        private async Task<bool> LegacyExists() =>
            (await (await _database.ListCollectionNamesAsync()).ToListAsync())
                .Contains(FunctionsConstants.LegacyRunStatsCollection);

        [Fact]
        public async Task Old_counts_are_added_to_runs_already_counted_on_the_function()
        {
            // Runs started since the new code shipped are on the function already; the old total
            // on top of them is the all-time count.
            var recent = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
            var old = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
            await Function("fn_1", totalRuns: 3, lastRunAt: recent);
            await Legacy.InsertOneAsync(new() { ItemId = "fn_1", TotalRuns = 100, LastRunAt = old });

            var moved = await Migration().MigrateTenantAsync(Tenant, CancellationToken.None);

            moved.Should().Be(1);
            var function = await Read("fn_1");
            function.TotalRuns.Should().Be(103);
            function.LastRunAt.Should().Be(recent, "an older carried-over time must not move it back");
        }

        [Fact]
        public async Task The_collection_is_dropped_once_everything_is_carried_over()
        {
            await Function("fn_1");
            await Function("fn_2");
            await Legacy.InsertManyAsync([new() { ItemId = "fn_1", TotalRuns = 5 }, new() { ItemId = "fn_2", TotalRuns = 7 }]);

            await Migration().MigrateTenantAsync(Tenant, CancellationToken.None);

            (await LegacyExists()).Should().BeFalse();
            (await Read("fn_2")).TotalRuns.Should().Be(7);
        }

        [Fact]
        public async Task Running_it_again_adds_nothing_twice()
        {
            await Function("fn_1");
            await Legacy.InsertOneAsync(new() { ItemId = "fn_1", TotalRuns = 5 });

            await Migration().MigrateTenantAsync(Tenant, CancellationToken.None);
            var second = await Migration().MigrateTenantAsync(Tenant, CancellationToken.None);

            second.Should().Be(0);
            (await Read("fn_1")).TotalRuns.Should().Be(5);
        }

        [Fact]
        public async Task A_failed_carry_over_puts_the_counts_back_and_keeps_the_collection()
        {
            await Legacy.InsertOneAsync(new() { ItemId = "fn_1", TotalRuns = 5 });
            var failing = new Mock<IFunctionRepository>();
            failing.Setup(f => f.ImportRunCountersAsync(Tenant, "fn_1", 5, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = async () => await Migration(failing.Object).MigrateTenantAsync(Tenant, CancellationToken.None);

            await act.Should().ThrowAsync<TimeoutException>();
            (await Legacy.Find(s => s.ItemId == "fn_1").FirstOrDefaultAsync())!.TotalRuns.Should().Be(5);
        }

        [Fact]
        public async Task A_tenant_that_never_had_the_collection_is_fine()
        {
            var moved = await Migration().MigrateTenantAsync(Tenant, CancellationToken.None);

            moved.Should().Be(0);
        }

        [Fact]
        public async Task Counting_a_run_is_not_an_edit()
        {
            // The list sorts by LastUpdatedDate; run traffic must not reorder it.
            await Function("fn_1");
            var startedAt = new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc);

            await _functions.RecordRunStartedAsync(Tenant, "fn_1", startedAt);
            await _functions.RecordRunStartedAsync(Tenant, "fn_1", startedAt.AddMinutes(-5));

            var function = await Read("fn_1");
            function.TotalRuns.Should().Be(2);
            function.LastRunAt.Should().Be(startedAt, "a late write for an earlier run cannot move it back");
            function.LastUpdatedDate.Should().Be(Updated);
        }

        [Fact]
        public async Task A_save_after_runs_does_not_lose_their_count()
        {
            // The reason the counters once lived apart: a whole-document write would undo them.
            await Function("fn_1");
            await _functions.RecordRunStartedAsync(Tenant, "fn_1", DateTime.UtcNow);

            await _functions.UpdateDetailsAsync(Tenant, "fn_1", "renamed", null, "u1");

            (await Read("fn_1")).TotalRuns.Should().Be(1);
        }

        [Fact]
        public async Task A_deleted_function_is_not_counted()
        {
            await Function("fn_1");
            await _functions.MarkDeletedAsync(Tenant, "fn_1", new FunctionDeletion { RequestedAt = DateTime.UtcNow });

            await _functions.RecordRunStartedAsync(Tenant, "fn_1", DateTime.UtcNow);

            (await Read("fn_1")).TotalRuns.Should().Be(0);
        }
    }
}
