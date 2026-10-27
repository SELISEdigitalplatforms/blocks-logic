using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Builds (RUNNER-FLOW step 1): newest wins, 3 at once, soft 5 min / hard 15 min, taken back after
    /// the hard limit + 30 s. The newest-wins check runs against a real Redis (TEST_REDIS; skipped without one).
    /// </summary>
    public sealed class BuildConsumerServiceTests : IAsyncLifetime
    {
        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private readonly string _tenant = $"t-{Guid.NewGuid():N}";

        public async Task InitializeAsync()
        {
            var host = Environment.GetEnvironmentVariable("TEST_REDIS") ?? "127.0.0.1:6379";
            try
            {
                var config = ConfigurationOptions.Parse(host);
                config.ConnectTimeout = 1500;
                config.AbortOnConnectFail = false;
                _redis = await ConnectionMultiplexer.ConnectAsync(config);
                if (!_redis.IsConnected) { _redis = null; return; }
                _db = _redis.GetDatabase();
                await _db.PingAsync();
            }
            catch (RedisException)
            {
                _redis = null;
                _db = null;
            }
        }

        public async Task DisposeAsync()
        {
            if (_db is not null) await _db.KeyDeleteAsync(RedisKeys.BuildLatest(_tenant, "fn-1"));
            if (_redis is not null) await _redis.DisposeAsync();
        }

        private static BuildConsumerService Service(IDatabase db, RunnerOptions? options = null)
        {
            var wrapped = Microsoft.Extensions.Options.Options.Create(options ?? new RunnerOptions());
            var room = new HostRoom(wrapped, new CalmHost(), NullLogger<HostRoom>.Instance);
            var heartbeat = new HeartbeatService(db, room,
                new StartupGuard(null!, db, wrapped, NullLogger<StartupGuard>.Instance),
                wrapped, NullLogger<HeartbeatService>.Instance);
            return new BuildConsumerService(db, null!, room, heartbeat, wrapped, NullLogger<BuildConsumerService>.Instance);
        }

        private BuildJob Job(string buildId) => new()
        {
            BuildId = buildId,
            FunctionId = "fn-1",
            TenantId = _tenant,
            SourceKey = "source",
            ImageRef = "image",
        };

        [Fact]
        public async Task A_build_is_stopped_once_a_newer_deploy_of_the_same_function_is_queued()
        {
            if (_db is null) return;
            var service = Service(_db);

            await _db.StringSetAsync(RedisKeys.BuildLatest(_tenant, "fn-1"), "build-1");
            (await service.IsSupersededAsync(Job("build-1"))).Should().BeFalse("it is the newest build");

            await _db.StringSetAsync(RedisKeys.BuildLatest(_tenant, "fn-1"), "build-2");
            (await service.IsSupersededAsync(Job("build-1"))).Should().BeTrue("a newer deploy replaced it");
            (await service.IsSupersededAsync(Job("build-2"))).Should().BeFalse();
        }

        [Fact]
        public async Task With_no_newest_build_recorded_a_build_goes_on()
        {
            if (_db is null) return;

            (await Service(_db).IsSupersededAsync(Job("build-1"))).Should().BeFalse("an older control plane sets no key");
        }

        [Fact]
        public void A_lost_build_is_taken_back_after_the_hard_limit_plus_30_seconds()
        {
            var options = new RunnerOptions();

            options.BuildTimeoutSeconds.Should().Be(900, "hard limit 15 min");
            options.BuildSlowWarningSeconds.Should().Be(300, "soft limit 5 min");
            options.MaxParallelBuilds.Should().Be(3);
            Service(null!, options).ReclaimAfter.Should().Be(TimeSpan.FromSeconds(930));
        }
    }
}
