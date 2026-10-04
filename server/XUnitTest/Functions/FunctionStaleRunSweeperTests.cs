using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The net under every way a run can be lost. What matters: stale runs are closed with a clear
    /// reason; fresh runs, terminal runs and runs whose payload still exists are not; a result that
    /// raced in between the read and the write wins; one bad tenant never stops the rest.
    /// </summary>
    public class FunctionStaleRunSweeperTests
    {
        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionTenantSource> _tenants = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();
        private readonly List<(string Tenant, RunStatus[] Statuses, DateTime Before, DateTime? NotBefore)> _queries = [];
        private readonly Dictionary<string, List<FunctionRunEntity>> _stale = new(StringComparer.Ordinal);

        public FunctionStaleRunSweeperTests()
        {
            _tenants.Setup(t => t.GetActiveTenantIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["t1"]);
            _redis.Fake.On("StringSetAsync", _ => true); // the sweep lock is free
            _runs.Setup(r => r.FindStaleCandidatesAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyCollection<RunStatus>>(), It.IsAny<DateTime>(),
                    It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string tenant, IReadOnlyCollection<RunStatus> statuses, DateTime before, DateTime? notBefore, int _, CancellationToken _) =>
                {
                    _queries.Add((tenant, statuses.ToArray(), before, notBefore));
                    // Behaves like the query: only candidates in those statuses, inside the window.
                    return _stale.GetValueOrDefault(tenant, [])
                        .Where(r => statuses.Contains(r.Status) && r.LastUpdatedDate < before
                                    && (notBefore == null || r.LastUpdatedDate >= notBefore))
                        .ToList();
                });
            _runs.Setup(r => r.CloseStaleAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunStatus>(), It.IsAny<int>(), It.IsAny<DateTime>(),
                    It.IsAny<RunStatus>(), It.IsAny<RunErrorCode>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        private FunctionStaleRunSweeper Sweeper(params (string Key, string Value)[] settings)
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build();
            return new FunctionStaleRunSweeper(
                cache.Object, _runs.Object, _tenants.Object, configuration, NullLogger<FunctionStaleRunSweeper>.Instance);
        }

        private FunctionRunEntity Add(string tenant, RunStatus status, TimeSpan age, string? id = null)
        {
            var run = new FunctionRunEntity
            {
                ItemId = id ?? Guid.NewGuid().ToString(),
                Status = status,
                Attempt = 2,
                LastUpdatedDate = DateTime.UtcNow - age,
            };
            if (!_stale.TryGetValue(tenant, out var list)) _stale[tenant] = list = [];
            list.Add(run);
            return run;
        }

        private void VerifyClosed(FunctionRunEntity run, RunStatus to, RunErrorCode code, Times times) =>
            _runs.Verify(r => r.CloseStaleAsync(
                It.IsAny<string>(), run.ItemId, run.Status, run.Attempt, run.LastUpdatedDate,
                to, code, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), times);

        private void VerifyNeverClosed(FunctionRunEntity run) =>
            _runs.Verify(r => r.CloseStaleAsync(
                It.IsAny<string>(), run.ItemId, It.IsAny<RunStatus>(), It.IsAny<int>(), It.IsAny<DateTime>(),
                It.IsAny<RunStatus>(), It.IsAny<RunErrorCode>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        [Fact]
        public async Task A_run_queued_past_the_payload_ttl_is_closed_as_abandoned()
        {
            var run = Add("t1", RunStatus.Queued, FunctionQueueKeys.RunTtl + TimeSpan.FromHours(1));
            // Even if something still holds a payload key, nothing can still be running it.
            _redis.Fake.On("KeyExistsAsync", _ => true);

            var closed = await Sweeper().SweepOnceAsync(CancellationToken.None);

            closed.Should().Be(1);
            VerifyClosed(run, RunStatus.Failed, RunErrorCode.Abandoned, Times.Once());
            _redis.Fake.Calls("PublishAsync").Should().ContainSingle("a waiting caller is woken");
        }

        [Fact]
        public async Task A_queued_run_whose_payload_is_gone_is_closed_early()
        {
            var run = Add("t1", RunStatus.Queued, TimeSpan.FromMinutes(20));
            _redis.Fake.On("KeyExistsAsync", _ => false);

            await Sweeper().SweepOnceAsync(CancellationToken.None);

            VerifyClosed(run, RunStatus.Failed, RunErrorCode.Abandoned, Times.Once());
        }

        [Fact]
        public async Task A_queued_run_whose_payload_still_exists_is_left_waiting()
        {
            var run = Add("t1", RunStatus.Queued, TimeSpan.FromMinutes(20));
            _redis.Fake.On("KeyExistsAsync", a => a[0]!.ToString() == FunctionQueueKeys.Run(run.ItemId));

            await Sweeper().SweepOnceAsync(CancellationToken.None);

            VerifyNeverClosed(run);
        }

        [Fact]
        public async Task Not_being_able_to_check_the_payload_is_not_taken_as_missing()
        {
            var run = Add("t1", RunStatus.Queued, TimeSpan.FromMinutes(20));
            _redis.Fake.On("KeyExistsAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

            await Sweeper().SweepOnceAsync(CancellationToken.None);

            VerifyNeverClosed(run);
        }

        [Fact]
        public async Task A_fresh_queued_run_is_not_touched()
        {
            var run = Add("t1", RunStatus.Queued, TimeSpan.FromMinutes(2));
            _redis.Fake.On("KeyExistsAsync", _ => false);

            await Sweeper().SweepOnceAsync(CancellationToken.None);

            VerifyNeverClosed(run);
        }

        [Fact]
        public async Task A_run_a_runner_never_reported_is_closed_as_timed_out()
        {
            var run = Add("t1", RunStatus.Running, TimeSpan.FromHours(1));

            await Sweeper().SweepOnceAsync(CancellationToken.None);

            VerifyClosed(run, RunStatus.TimedOut, RunErrorCode.Abandoned, Times.Once());
        }

        [Fact]
        public async Task A_running_run_inside_its_timeout_is_not_touched()
        {
            var run = Add("t1", RunStatus.Running, TimeSpan.FromSeconds(FunctionLimits.Ceiling.TimeoutSeconds));

            await Sweeper().SweepOnceAsync(CancellationToken.None);

            VerifyNeverClosed(run);
        }

        [Fact]
        public async Task Output_delivery_that_never_finished_is_closed_as_output_failed()
        {
            var run = Add("t1", RunStatus.OutputProcessing, TimeSpan.FromHours(7));
            var recent = Add("t1", RunStatus.OutputProcessing, TimeSpan.FromHours(1));

            await Sweeper().SweepOnceAsync(CancellationToken.None);

            VerifyClosed(run, RunStatus.OutputFailed, RunErrorCode.OutputActionFailed, Times.Once());
            VerifyNeverClosed(recent);
        }

        [Fact]
        public async Task Terminal_runs_are_never_even_asked_for()
        {
            await Sweeper().SweepOnceAsync(CancellationToken.None);

            _queries.Should().NotBeEmpty();
            _queries.SelectMany(q => q.Statuses).Should().OnlyContain(s => !FunctionWireMapping.IsTerminal(s));
        }

        [Fact]
        public async Task A_result_that_raced_in_wins_and_is_not_counted()
        {
            var run = Add("t1", RunStatus.Running, TimeSpan.FromHours(1));
            _runs.Setup(r => r.CloseStaleAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunStatus>(), It.IsAny<int>(), It.IsAny<DateTime>(),
                    It.IsAny<RunStatus>(), It.IsAny<RunErrorCode>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var closed = await Sweeper().SweepOnceAsync(CancellationToken.None);

            closed.Should().Be(0);
            // The close was conditional on exactly what was read — status, attempt and timestamp.
            VerifyClosed(run, RunStatus.TimedOut, RunErrorCode.Abandoned, Times.Once());
            _redis.Fake.Calls("PublishAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task One_failing_tenant_does_not_stop_the_others()
        {
            _tenants.Setup(t => t.GetActiveTenantIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["bad", "t1"]);
            _runs.Setup(r => r.FindStaleCandidatesAsync(
                    "bad", It.IsAny<IReadOnlyCollection<RunStatus>>(), It.IsAny<DateTime>(),
                    It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("tenant database unreachable"));
            var run = Add("t1", RunStatus.Running, TimeSpan.FromHours(1));

            var closed = await Sweeper().SweepOnceAsync(CancellationToken.None);

            closed.Should().Be(1);
            VerifyClosed(run, RunStatus.TimedOut, RunErrorCode.Abandoned, Times.Once());
        }

        [Fact]
        public async Task Another_worker_holding_the_lock_means_this_one_skips()
        {
            _redis.Fake.On("StringSetAsync", _ => false);
            Add("t1", RunStatus.Running, TimeSpan.FromHours(1));

            var closed = await Sweeper().SweepOnceAsync(CancellationToken.None);

            closed.Should().Be(0);
            _tenants.Verify(t => t.GetActiveTenantIdsAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Without_redis_for_the_lock_it_sweeps_anyway()
        {
            _redis.Fake.On("StringSetAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));
            var run = Add("t1", RunStatus.Running, TimeSpan.FromHours(1));

            await Sweeper().SweepOnceAsync(CancellationToken.None);

            VerifyClosed(run, RunStatus.TimedOut, RunErrorCode.Abandoned, Times.Once());
        }

        [Fact]
        public async Task Failing_to_list_tenants_is_survived()
        {
            _tenants.Setup(t => t.GetActiveTenantIdsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("root db"));

            var closed = await Sweeper().SweepOnceAsync(CancellationToken.None);

            closed.Should().Be(0);
        }

        [Fact]
        public void Deadlines_default_sanely_and_cannot_be_configured_below_their_floors()
        {
            var defaults = Sweeper().ResolveSettings();
            defaults.Interval.Should().Be(TimeSpan.FromSeconds(FunctionStaleRunSweeper.DefaultIntervalSeconds));
            defaults.QueuedDeadline.Should().BeGreaterThan(FunctionQueueKeys.RunTtl);
            defaults.ExecutingDeadline.Should().BeGreaterThan(TimeSpan.FromSeconds(FunctionLimits.Ceiling.TimeoutSeconds));

            var tiny = Sweeper(
                ("Functions:StaleRunSweepIntervalSeconds", "1"),
                ("Functions:StaleRunMarginSeconds", "0"),
                ("Functions:StaleRunMissingPayloadMinutes", "0"),
                ("Functions:StaleOutputProcessingMinutes", "1"),
                ("Functions:StaleRunSweepBatchSize", "0")).ResolveSettings();
            tiny.Interval.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(30));
            tiny.MissingPayloadDeadline.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(5));
            tiny.OutputDeadline.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
            tiny.ExecutingDeadline.Should().BeGreaterThan(TimeSpan.FromSeconds(FunctionLimits.Ceiling.TimeoutSeconds + 60));
            tiny.QueuedDeadline.Should().BeGreaterThan(FunctionQueueKeys.RunTtl);
            tiny.BatchSize.Should().BeGreaterThanOrEqualTo(1);
        }
    }
}
