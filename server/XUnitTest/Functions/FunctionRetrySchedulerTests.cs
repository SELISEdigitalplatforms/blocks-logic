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
            ClaimsAre(1);
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)"{\"run\":{\"attempt\":1}}");
            _runs.Setup(r => r.GetByIdAsync("t1", "run-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionRunEntity { ItemId = "run-1", Attempt = 1, Status = RunStatus.Failed });
            _versions.Setup(v => v.GetByIdAsync("t1", "v-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionVersionEntity { ItemId = "v-1", ImageDigest = "sha256:abc" });
            _runs.Setup(r => r.ResetForRetryAsync("t1", "run-1", 2, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }

        private readonly Mock<global::Functions.DomainService.Storage.IFunctionArtifactStore> _artifacts = new();

        /// <summary>The claim script answers <paramref name="claims"/>; the release script answers 1.</summary>
        private void ClaimsAre(long claims) =>
            _redis.Fake.On("ScriptEvaluateAsync", args =>
                RedisResult.Create((RedisValue)(args[0]!.ToString() == FunctionRetryScheduler.ClaimScript ? claims : 1)));

        private int Released() =>
            _redis.Fake.Calls("ScriptEvaluateAsync").Count(a => a[0]!.ToString() == FunctionRetryScheduler.DoneScript);

        private FunctionRetryScheduler Scheduler()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            return new FunctionRetryScheduler(cache.Object, _runs.Object, _versions.Object,
                NullLogger<FunctionRetryScheduler>.Instance, _artifacts.Object);
        }

        private void VersionIs(FunctionVersionEntity version) =>
            _versions.Setup(v => v.GetByIdAsync("t1", "v-1", It.IsAny<CancellationToken>())).ReturnsAsync(version);

        private static string? Field(NameValueEntry[] entry, string name) =>
            entry.Any(e => e.Name == name) ? entry.First(e => e.Name == name).Value.ToString() : null;

        [Fact]
        public async Task An_artifact_only_version_is_retried_with_its_run_image_and_a_fresh_artifact_url()
        {
            VersionIs(new FunctionVersionEntity { ItemId = "v-1", ArtifactId = "ART-1", ArtifactSha256 = "deadbeef" });
            _artifacts.Setup(a => a.CreateDownloadUrlAsync("t1", "ART-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://blob/art-1?sig=x");

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            var entry = (NameValueEntry[])_redis.Fake.Calls("StreamAddAsync").Single()[1]!;
            Field(entry, "image").Should().Be("blocks-fn-artifact/art-1:local");
            Field(entry, FunctionQueueKeys.RunArtifactUrlField).Should().Be("https://blob/art-1?sig=x");
            Field(entry, FunctionQueueKeys.RunArtifactSha256Field).Should().Be("deadbeef");
        }

        [Fact]
        public async Task An_artifact_missing_from_the_store_still_retries_by_its_image_name()
        {
            VersionIs(new FunctionVersionEntity { ItemId = "v-1", ArtifactId = "ART-1" });
            _artifacts.Setup(a => a.CreateDownloadUrlAsync("t1", "ART-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            var entry = (NameValueEntry[])_redis.Fake.Calls("StreamAddAsync").Single()[1]!;
            Field(entry, "image").Should().Be("blocks-fn-artifact/art-1:local");
            Field(entry, FunctionQueueKeys.RunArtifactUrlField).Should().BeNull();
        }

        [Fact]
        public async Task A_version_with_neither_image_nor_artifact_is_not_retried_and_not_reset()
        {
            VersionIs(new FunctionVersionEntity { ItemId = "v-1" });

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
            _runs.Verify(r => r.ResetForRetryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_registry_built_version_retries_exactly_as_before_without_artifact_fields()
        {
            await Scheduler().SweepOnceAsync(CancellationToken.None);

            var entry = (NameValueEntry[])_redis.Fake.Calls("StreamAddAsync").Single()[1]!;
            Field(entry, "image").Should().Be("sha256:abc");
            Field(entry, FunctionQueueKeys.RunArtifactUrlField).Should().BeNull();
            _artifacts.VerifyNoOtherCalls();
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
    
        [Fact]
        public async Task A_run_that_asked_for_a_warm_sandbox_keeps_asking_on_its_retry()
        {
            _runs.Setup(r => r.GetByIdAsync("t1", "run-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionRunEntity { ItemId = "run-1", Attempt = 1, Status = RunStatus.Failed, ReuseRequested = true });

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            var entry = (NameValueEntry[])_redis.Fake.Calls("StreamAddAsync").Single()[1]!;
            entry.Single(e => e.Name == FunctionQueueKeys.RunReuseField).Value.ToString().Should().Be("1");
        }

        [Fact]
        public async Task A_due_retry_is_claimed_not_removed_and_released_only_once_enqueued()
        {
            await Scheduler().SweepOnceAsync(CancellationToken.None);

            _redis.Fake.Calls("SortedSetRemoveAsync").Should().BeEmpty();
            var calls = _redis.Fake.CallNames.ToList();
            calls.IndexOf("StreamAddAsync").Should().BeLessThan(calls.LastIndexOf("ScriptEvaluateAsync"));
            Released().Should().Be(1);
        }

        [Fact]
        public async Task A_retry_another_worker_holds_is_left_alone()
        {
            ClaimsAre(0);

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            _runs.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Released().Should().Be(0);
        }

        [Fact]
        public async Task A_retry_whose_run_cannot_be_read_stays_claimed_and_comes_back_later()
        {
            // The FN-16 loss: the entry used to be removed before this read, so a Mongo error (or a
            // Worker stopping here) lost the retry for good.
            _runs.Setup(r => r.GetByIdAsync("t1", "run-1", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = () => Scheduler().SweepOnceAsync(CancellationToken.None);

            await act.Should().NotThrowAsync();
            Released().Should().Be(0);
            _redis.Fake.Calls("SortedSetRemoveAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task A_reclaimed_retry_that_was_already_enqueued_is_not_enqueued_twice()
        {
            // The Worker stopped after the enqueue but before releasing: the run is already on the
            // new attempt, so the second pass only lets go.
            ClaimsAre(2);
            _runs.Setup(r => r.GetByIdAsync("t1", "run-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionRunEntity { ItemId = "run-1", Attempt = 2, Status = RunStatus.Queued });

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
            Released().Should().Be(1);
        }

        [Fact]
        public async Task A_retry_that_never_completes_is_given_up_after_the_claim_limit()
        {
            ClaimsAre(FunctionRetryScheduler.MaxClaims + 1);

            await Scheduler().SweepOnceAsync(CancellationToken.None);

            _runs.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Released().Should().Be(1);
        }

        [Fact]
        public async Task A_retry_that_cannot_be_released_is_still_enqueued_once()
        {
            _redis.Fake.On("ScriptEvaluateAsync", args => args[0]!.ToString() == FunctionRetryScheduler.ClaimScript
                ? RedisResult.Create((RedisValue)1)
                : throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

            var act = () => Scheduler().SweepOnceAsync(CancellationToken.None);

            await act.Should().NotThrowAsync();
            _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle();
        }

        [Fact]
        public async Task A_run_that_did_not_ask_for_reuse_retries_with_the_entry_exactly_as_before()
        {
            await Scheduler().SweepOnceAsync(CancellationToken.None);

            var entry = (NameValueEntry[])_redis.Fake.Calls("StreamAddAsync").Single()[1]!;
            entry.Select(e => e.Name.ToString()).Should().Equal(
                "runId", "functionId", "versionId", "tenantId", "image", "attempt", "protocol");
        }
}
}
