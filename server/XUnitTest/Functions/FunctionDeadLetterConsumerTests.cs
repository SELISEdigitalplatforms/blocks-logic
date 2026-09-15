using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The other end of the runner's dead letter. Before this consumer existed a job the runner
    /// could never deliver left its Mongo record QUEUED for ever, so the assertions worth having
    /// are that the record is actually closed, that a stale dead letter cannot overwrite a real
    /// outcome, and that nothing here re-enqueues work whose delivery budget is already spent.
    /// </summary>
    public class FunctionDeadLetterConsumerTests
    {
        private readonly Mock<IDatabase> _database = new(MockBehavior.Loose);
        private readonly Mock<IFunctionRunRepository> _runs = new(MockBehavior.Loose);
        private readonly Mock<IFunctionBuildRepository> _builds = new(MockBehavior.Loose);

        public FunctionDeadLetterConsumerTests()
        {
            _database
                .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);
            _runs
                .Setup(r => r.FailIfNotTerminalAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunErrorCode>(),
                    It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _builds
                .Setup(b => b.FailIfNotTerminalAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        private FunctionDeadLetterConsumer Consumer()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_database.Object);
            return new FunctionDeadLetterConsumer(
                cache.Object, _runs.Object, _builds.Object, NullLogger<FunctionDeadLetterConsumer>.Instance);
        }

        private static ResultStreamEntry Entry(string id, params (string Key, string Value)[] fields) =>
            new(id, fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal));

        private static ResultStreamEntry DeadRun(string reason = "delivered 4 times without completing") =>
            Entry("1700000000000-0",
                ("deadReason", reason),
                ("sourceStream", FunctionQueueKeys.RunsStream),
                ("runId", "run-1"),
                ("functionId", "fn-1"),
                ("tenantId", "tenant-1"));

        private static ResultStreamEntry DeadBuild(string reason = "the entry carries no imageRef") =>
            Entry("1700000000001-0",
                ("deadReason", reason),
                ("sourceStream", FunctionQueueKeys.BuildsStream),
                ("buildId", "build-1"),
                ("functionId", "fn-1"),
                ("tenantId", "tenant-1"));

        [Fact]
        public async Task A_dead_lettered_run_is_closed_as_failed_and_undeliverable()
        {
            await Consumer().ProcessAsync(DeadRun(), CancellationToken.None);

            _runs.Verify(r => r.FailIfNotTerminalAsync(
                "tenant-1", "run-1", RunErrorCode.Undeliverable,
                It.Is<string>(m => m.Contains("never executed", StringComparison.Ordinal)),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task The_runners_own_reason_reaches_the_developer()
        {
            await Consumer().ProcessAsync(DeadRun("protocol version 2 is not supported by this runner"), CancellationToken.None);

            _runs.Verify(r => r.FailIfNotTerminalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunErrorCode>(),
                It.Is<string>(m => m.Contains("protocol version 2", StringComparison.Ordinal)),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Closing_a_run_wakes_a_caller_waiting_synchronously()
        {
            // Without the publish a sync caller inside its wait window sits out the full timeout
            // on a run that is already decided.
            await Consumer().ProcessAsync(DeadRun(), CancellationToken.None);

            _database.Verify(d => d.PublishAsync(
                RedisChannel.Literal(FunctionQueueKeys.SyncChannel("run-1")),
                (RedisValue)FunctionQueueKeys.Wire.Failed,
                CommandFlags.None), Times.Once);
        }

        [Fact]
        public async Task A_dead_lettered_build_is_closed_as_failed()
        {
            await Consumer().ProcessAsync(DeadBuild(), CancellationToken.None);

            _builds.Verify(b => b.FailIfNotTerminalAsync(
                "tenant-1", "build-1",
                It.Is<string>(m => m.Contains("never ran", StringComparison.Ordinal)),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_run_that_already_reported_is_left_alone()
        {
            // The repository refuses the write because the run is terminal; nothing else must
            // happen either — least of all waking a sync waiter with a false FAILED.
            _runs
                .Setup(r => r.FailIfNotTerminalAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunErrorCode>(),
                    It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            await Consumer().ProcessAsync(DeadRun(), CancellationToken.None);

            _database.Verify(d => d.PublishAsync(
                It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task Nothing_is_ever_re_enqueued_for_retry()
        {
            // The delivery budget is spent by definition. A retry here would put the same
            // poisonous job back on the same stream, on a timer.
            await Consumer().ProcessAsync(DeadRun(), CancellationToken.None);

            _database.Verify(d => d.SortedSetAddAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<double>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Never);
            _database.Verify(d => d.StreamAddAsync(
                It.IsAny<RedisKey>(), It.IsAny<NameValueEntry[]>(), It.IsAny<RedisValue?>(),
                It.IsAny<long?>(), It.IsAny<bool>(), It.IsAny<long?>(), It.IsAny<StreamTrimMode>(),
                It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public void Undeliverable_is_not_a_retryable_error_code()
        {
            // Belt and braces for the test above: even if such a code ever arrived through the
            // results stream instead, the retry path must not pick it up.
            FunctionWireMapping.IsRetryable(RunStatus.Failed, RunErrorCode.Undeliverable).Should().BeFalse();
        }

        [Fact]
        public async Task An_entry_with_no_tenant_is_logged_and_dropped_rather_than_retried_for_ever()
        {
            // Database-per-tenant: there is no collection to write to, and no amount of retrying
            // will produce one.
            var entry = Entry("1700000000002-0",
                ("deadReason", "the entry carries no runId"),
                ("sourceStream", FunctionQueueKeys.RunsStream));

            await Consumer().ProcessAsync(entry, CancellationToken.None);

            _runs.VerifyNoOtherCalls();
            _builds.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task An_already_applied_entry_is_not_applied_twice()
        {
            // The dead stream keeps its entries, so a reclaim can present the same one again.
            _database
                .Setup(d => d.KeyExistsAsync(
                    (RedisKey)FunctionQueueKeys.DeadApplied("1700000000000-0"), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            await Consumer().ProcessAsync(DeadRun(), CancellationToken.None);

            _runs.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task The_applied_marker_is_written_only_after_the_record_is_closed()
        {
            var order = new List<string>();
            _runs
                .Setup(r => r.FailIfNotTerminalAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunErrorCode>(),
                    It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { order.Add("close"); return true; });
            _database
                .Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                    It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(() => { order.Add("mark"); return true; });

            await Consumer().ProcessAsync(DeadRun(), CancellationToken.None);

            // A marker written first would suppress the retry of an update that then failed.
            order.Should().ContainInOrder("close", "mark");
        }

        [Fact]
        public async Task An_entry_from_an_older_runner_with_no_source_stream_is_still_closed()
        {
            var entry = Entry("1700000000003-0",
                ("deadReason", "delivered 4 times without completing"),
                ("runId", "run-9"),
                ("tenantId", "tenant-1"));

            await Consumer().ProcessAsync(entry, CancellationToken.None);

            _runs.Verify(r => r.FailIfNotTerminalAsync(
                "tenant-1", "run-9", RunErrorCode.Undeliverable, It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task An_entry_naming_neither_a_run_nor_a_build_closes_nothing()
        {
            var entry = Entry("1700000000004-0", ("deadReason", "who knows"), ("tenantId", "tenant-1"));

            await Consumer().ProcessAsync(entry, CancellationToken.None);

            _runs.VerifyNoOtherCalls();
            _builds.VerifyNoOtherCalls();

            // And it leaves no marker, so a later fix can still apply it.
            _database.Verify(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public void The_repository_filter_lists_the_same_terminal_statuses_as_the_wire_mapping()
        {
            // The filter needs a set and IsTerminal is a predicate, so the two cannot share an
            // implementation — but they must not disagree, or a dead letter would overwrite a
            // status one of them considers final.
            var all = Enum.GetValues<RunStatus>();
            var fromMapping = all.Where(FunctionWireMapping.IsTerminal);

            FunctionRunRepository.TerminalStatuses.Should().BeEquivalentTo(fromMapping);
        }
    }
}
