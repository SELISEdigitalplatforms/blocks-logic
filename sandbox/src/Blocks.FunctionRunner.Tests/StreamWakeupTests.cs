using System.Diagnostics;
using Blocks.FunctionRunner.Redis;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The pub/sub nudge that wakes an idle stream reader (runs, results) instead of letting it
    /// sleep its 250 ms poll — against a real Redis (skipping cleanly without one; set TEST_REDIS).
    /// The poll must stay the floor: no nudge means exactly the old wait.
    /// </summary>
    public sealed class StreamWakeupTests : IAsyncLifetime
    {
        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private readonly string _channel = $"test:nudge:{Guid.NewGuid():N}";

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
            if (_redis is not null) await _redis.DisposeAsync();
        }

        private bool Unavailable => _db is null;

        [SkippableFact]
        public async Task A_nudge_wakes_the_reader_long_before_its_poll_interval()
        {
            Skip.If(Unavailable, "no Redis available");
            using var wakeup = await StreamWakeup.SubscribeAsync(_db!, _channel);

            var watch = Stopwatch.StartNew();
            var waiting = wakeup.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            StreamWakeup.Publish(_db!, _channel);
            await waiting;

            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        }

        [SkippableFact]
        public async Task Without_a_nudge_the_reader_waits_its_whole_interval()
        {
            Skip.If(Unavailable, "no Redis available");
            using var wakeup = await StreamWakeup.SubscribeAsync(_db!, _channel);

            var watch = Stopwatch.StartNew();
            await wakeup.WaitAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);

            watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180));
        }

        [SkippableFact]
        public async Task A_nudge_while_busy_is_remembered_once_not_counted()
        {
            Skip.If(Unavailable, "no Redis available");
            using var wakeup = await StreamWakeup.SubscribeAsync(_db!, _channel);

            for (var i = 0; i < 3; i++) StreamWakeup.Publish(_db!, _channel);
            await Task.Delay(200); // let the three arrive while nobody waits

            var watch = Stopwatch.StartNew();
            await wakeup.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100), "the remembered nudge returns at once");

            watch.Restart();
            await wakeup.WaitAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);
            watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180), "only one was remembered");
        }

        [Fact]
        public async Task Publishing_on_a_dead_connection_never_throws_and_the_poll_still_works()
        {
            var config = ConfigurationOptions.Parse("127.0.0.1:1");
            config.AbortOnConnectFail = false;
            config.ConnectTimeout = 200;
            await using var dead = await ConnectionMultiplexer.ConnectAsync(config);
            var db = dead.GetDatabase();

            var act = () => StreamWakeup.Publish(db, _channel);
            act.Should().NotThrow();

            using var wakeup = await StreamWakeup.SubscribeAsync(db, _channel);
            var watch = Stopwatch.StartNew();
            await wakeup.WaitAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);
            watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(130));
        }

        [Fact]
        public void The_handover_log_names_each_step_and_never_a_value()
        {
            var logs = new CapturingLoggerProvider();
            var timings = new HandoverTimings(Stopwatch.StartNew());
            timings.ImageReady();
            timings.Admitted();
            timings.Secrets(12);
            timings.Token(345);

            timings.Log(logs.For<HandoverTimings>(), "run_1", 400, reused: true);

            var line = logs.All;
            line.Should().Contain("run_1").And.Contain("handover 400 ms").And.Contain("secrets 12 ms")
                .And.Contain("token 345 ms").And.Contain("sandbox -1 ms", "a step never reached is -1, not a guess");
        }
    }
}
