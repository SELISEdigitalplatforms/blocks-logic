using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A retry either lands on the queue or is closed — it is never left QUEUED with nothing
    /// behind it — and a retry another Worker already started is not started again.
    /// </summary>
    public class FunctionRetrySchedulerTests
    {
        private const string Member = "{\"runId\":\"run-1\",\"tenantId\":\"t1\",\"functionId\":\"fn-1\",\"versionId\":\"v-1\",\"attempt\":2}";

        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionVersionRepository> _versions = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();

        public FunctionRetrySchedulerTests()
        {
            _redis.Fake.On("SortedSetRangeByScoreAsync", _ => new RedisValue[] { Member });
            _redis.Fake.On("SortedSetRemoveAsync", _ => true);
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)"{\"run\":{\"attempt\":1}}");
            _runs.Setup(r => r.GetByIdAsync("t1", "run-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionRunEntity { ItemId = "run-1", Attempt = 1, Status = RunStatus.Failed });
            _versions.Setup(v => v.GetByIdAsync("t1", "v-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionVersionEntity { ItemId = "v-1", ImageDigest = "sha256:abc" });
            _runs.Setup(r => r.ResetForRetryAsync("t1", "run-1", 2, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }

        private FunctionRetryScheduler Scheduler()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            return new FunctionRetryScheduler(cache.Object, _runs.Object, _versions.Object, NullLogger<FunctionRetryScheduler>.Instance);
        }

        [Fact]
        public async Task A_due_retry_is_reset_and_enqueued()
        {
            await Scheduler().SweepOnceAsync(CancellationToken.None);

            _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle(a => a[0]!.ToString() == FunctionQueueKeys.RunsStream);
        }

        [Fact]
        public async Task A_retry_reuses_the_references_so_the_runner_resolves_them_afresh()
        {
            // The stored envelope holds references only; the retry copies it with just the
            // attempt patched, so the new attempt carries references too — never plaintext — and
            // the runner resolves them again (picking up a rotated secret).
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)
                "{\"run\":{\"attempt\":1},\"env\":{\"AUTH\":\"Bearer {{secret.sec_1}}\"},\"maskedEnv\":[\"AUTH\"]}");

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            var envelope = _redis.Fake.Calls("HashSetAsync")
                .SelectMany(c => (HashEntry[])c[1]!).Single(e => e.Name == "envelope").Value.ToString();
            using var doc = System.Text.Json.JsonDocument.Parse(envelope);
            doc.RootElement.GetProperty("env").GetProperty("AUTH").GetString().Should().Be("Bearer {{secret.sec_1}}");
            doc.RootElement.GetProperty("run").GetProperty("attempt").GetInt32().Should().Be(2);

            var entry = (NameValueEntry[])_redis.Fake.Calls("StreamAddAsync").Single()[1]!;
            entry.Single(e => e.Name == "protocol").Value.ToString()
                .Should().Be(FunctionQueueKeys.RunProtocolVersion.ToString());
        }

        [Fact]
        public async Task A_retry_another_worker_already_started_is_not_enqueued_again()
        {
            _runs.Setup(r => r.ResetForRetryAsync("t1", "run-1", 2, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
            _redis.Fake.Calls("HashSetAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task A_retry_that_cannot_be_enqueued_is_closed_not_stranded()
        {
            _redis.Fake.On("StreamAddAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            _runs.Verify(r => r.FailIfNotTerminalAsync(
                "t1", "run-1", RunErrorCode.EnqueueFailed, It.Is<string>(m => m.Contains("attempt 2", StringComparison.Ordinal)),
                It.IsAny<DateTime>(), CancellationToken.None), Times.Once);
            _redis.Fake.Calls("KeyDeleteAsync").Should().ContainSingle(a => a[0]!.ToString() == FunctionQueueKeys.Run("run-1"));
        }

        [Fact]
        public async Task Failing_to_close_it_as_well_does_not_break_the_sweep()
        {
            _redis.Fake.On("StreamAddAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));
            _runs.Setup(r => r.FailIfNotTerminalAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunErrorCode>(), It.IsAny<string>(),
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = () => Scheduler().SweepOnceAsync(CancellationToken.None);

            await act.Should().NotThrowAsync();
        }
    }
}
