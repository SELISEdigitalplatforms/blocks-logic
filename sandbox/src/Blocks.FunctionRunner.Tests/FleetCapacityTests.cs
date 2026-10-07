using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The tenant share is a share of the fleet (FN-17, 2026-10-07): the sum of live runners'
    /// capacity from their heartbeats, never below this host's own, and this host's own whenever
    /// the fleet cannot be read. Against a real Redis (TEST_REDIS; skipped without one).
    /// </summary>
    [Collection("fleet")]
    public sealed class FleetCapacityTests : IAsyncLifetime
    {
        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private readonly List<string> _runners = [];
        private readonly ManualTime _time = new();

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
                await _db.KeyDeleteAsync(RedisKeys.Runners);
            }
            catch (RedisException)
            {
                _redis = null;
                _db = null;
            }
        }

        public async Task DisposeAsync()
        {
            if (_db is not null)
            {
                foreach (var id in _runners) await _db.KeyDeleteAsync(RedisKeys.Runner(id));
                await _db.KeyDeleteAsync(RedisKeys.Runners);
            }
            if (_redis is not null) await _redis.DisposeAsync();
        }

        private bool Unavailable => _db is null;

        private FleetCapacity Fleet(string runnerId) =>
            new(Microsoft.Extensions.Options.Options.Create(new RunnerOptions { RunnerId = runnerId, HeartbeatMs = 5000 }), _time);

        /// <summary>A heartbeat as HeartbeatService writes it, refreshed <paramref name="agoMs"/> ago.</summary>
        private async Task BeatAsync(string runnerId, int capacity, int agoMs = 0)
        {
            _runners.Add(runnerId);
            var key = RedisKeys.Runner(runnerId);
            await _db!.HashSetAsync(key, [new HashEntry("runnerId", runnerId), new HashEntry("capacity", capacity)]);
            await _db.KeyExpireAsync(key, RedisKeys.HeartbeatTtl - TimeSpan.FromMilliseconds(agoMs));
            await _db.SetAddAsync(RedisKeys.Runners, runnerId);
        }

        [SkippableFact]
        public async Task The_share_is_half_of_every_live_runner_together_whatever_each_host_is()
        {
            Skip.If(Unavailable, "no Redis available");
            await BeatAsync("big", 10);
            await BeatAsync("small", 4, agoMs: 4000);
            var options = new RunnerOptions();

            var onBig = Fleet("big");
            await onBig.RefreshAsync(_db!);
            var onSmall = Fleet("small");
            await onSmall.RefreshAsync(_db!);

            onBig.For(local: 10).Should().Be(14);
            onSmall.For(local: 4).Should().Be(14);
            options.TenantSlotLimit(onSmall.For(4)).Should().Be(7, "the same limit on every host, not 5 here and 2 there");
        }

        [SkippableFact]
        public async Task A_crashed_runner_stops_counting_once_its_heartbeat_is_stale_and_leaves_the_set_when_gone()
        {
            Skip.If(Unavailable, "no Redis available");
            await BeatAsync("alive", 10);
            await BeatAsync("crashed", 10, agoMs: 9000);   // last beat 9 s ago: past the ~8 s window
            await _db!.SetAddAsync(RedisKeys.Runners, "vanished");   // its hash already expired

            var fleet = Fleet("alive");
            await fleet.RefreshAsync(_db);

            fleet.For(local: 10).Should().Be(10);
            (await _db.SetContainsAsync(RedisKeys.Runners, "vanished")).Should().BeFalse("cleaned up by the reader");
            (await _db.SetContainsAsync(RedisKeys.Runners, "crashed")).Should().BeTrue("its key may still be refreshed");
        }

        [SkippableFact]
        public async Task A_clean_stop_leaves_the_set_at_once()
        {
            Skip.If(Unavailable, "no Redis available");
            var fleet = Fleet("leaving");
            await fleet.JoinAsync(_db!);
            (await _db!.SetContainsAsync(RedisKeys.Runners, "leaving")).Should().BeTrue();

            await fleet.LeaveAsync(_db);

            (await _db.SetContainsAsync(RedisKeys.Runners, "leaving")).Should().BeFalse();
        }

        [SkippableFact]
        public async Task Without_a_fresh_reading_the_hosts_own_capacity_is_used()
        {
            Skip.If(Unavailable, "no Redis available");
            var fleet = Fleet("me");
            fleet.For(local: 6).Should().Be(6, "never read");

            await BeatAsync("me", 6);
            await BeatAsync("other", 6);
            await fleet.RefreshAsync(_db!);
            fleet.For(local: 6).Should().Be(12);

            _time.Advance(TimeSpan.FromSeconds(16));
            fleet.For(local: 6).Should().Be(6, "older than three heartbeats: Redis may be failing");
        }

        [SkippableFact]
        public async Task A_reading_smaller_than_this_host_is_not_believed()
        {
            Skip.If(Unavailable, "no Redis available");
            await BeatAsync("tiny", 2);
            var fleet = Fleet("me-not-yet-beating");
            await fleet.RefreshAsync(_db!);

            fleet.For(local: 8).Should().Be(8);
        }

        [Fact]
        public void A_fixed_share_is_for_the_whole_fleet_and_ignores_capacity()
        {
            new RunnerOptions { MaxSandboxesPerTenant = 3 }.TenantSlotLimit(40).Should().Be(3);
            new RunnerOptions().TenantSlotLimit(1).Should().Be(1);
        }
    }
}
