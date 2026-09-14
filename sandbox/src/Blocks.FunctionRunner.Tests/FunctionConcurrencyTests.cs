using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Runs against the local Redis when one is reachable, and skips cleanly otherwise. These
    /// need a real server because the whole point is the Lua and the expiry behaviour, which a
    /// fake would only re-implement — and it was exactly that behaviour that was wrong.
    /// </summary>
    public sealed class FunctionConcurrencyTests : IAsyncLifetime
    {
        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private string _functionId = string.Empty;

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

            _functionId = $"fn_test_{Guid.NewGuid():N}";
        }

        public async Task DisposeAsync()
        {
            if (_db is not null) await _db.KeyDeleteAsync(RedisKeys.Concurrency(_functionId));
            if (_redis is not null) await _redis.DisposeAsync();
        }

        private bool Unavailable => _db is null;

        [SkippableFact]
        public async Task Admits_up_to_the_limit_and_no_further()
        {
            Skip.If(Unavailable, "no Redis available");

            var a = await FunctionConcurrency.TryEnterAsync(_db!, _functionId, "run_a", 2);
            var b = await FunctionConcurrency.TryEnterAsync(_db!, _functionId, "run_b", 2);
            var c = await FunctionConcurrency.TryEnterAsync(_db!, _functionId, "run_c", 2);

            a.Should().NotBeNull();
            b.Should().NotBeNull();
            c.Should().BeNull("the function is already running its limit of 2");

            await a!.DisposeAsync();
            await b!.DisposeAsync();
        }

        [SkippableFact]
        public async Task A_released_slot_is_immediately_reusable()
        {
            Skip.If(Unavailable, "no Redis available");

            var a = await FunctionConcurrency.TryEnterAsync(_db!, _functionId, "run_a", 1);
            (await FunctionConcurrency.TryEnterAsync(_db!, _functionId, "run_b", 1)).Should().BeNull();

            await a!.DisposeAsync();

            var b = await FunctionConcurrency.TryEnterAsync(_db!, _functionId, "run_b", 1);
            b.Should().NotBeNull();
            await b!.DisposeAsync();
        }

        [SkippableFact]
        public async Task The_same_run_re_entering_does_not_consume_a_second_slot()
        {
            Skip.If(Unavailable, "no Redis available");

            // A reclaimed run must be able to finish what it started without needing new capacity.
            var first = await FunctionConcurrency.TryEnterAsync(_db!, _functionId, "run_a", 1);
            first.Should().NotBeNull();

            var again = await FunctionConcurrency.TryEnterAsync(_db!, _functionId, "run_a", 1);
            again.Should().NotBeNull("the same run already holds this slot");

            (await FunctionConcurrency.HeldAsync(_db!, _functionId)).Should().Be(1);

            await first!.DisposeAsync();
            await again!.DisposeAsync();
        }

        [SkippableFact]
        public async Task A_leaked_slot_does_not_take_the_others_with_it()
        {
            Skip.If(Unavailable, "no Redis available");

            // This is the regression. A runner killed mid-run leaks its slot; that must cost one
            // slot for one lifetime, not block the function and not wipe live slots when the key
            // eventually expires. Simulated by planting an already-expired member.
            var key = RedisKeys.Concurrency(_functionId);
            var past = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds();
            await _db!.SortedSetAddAsync(key, "run_crashed", past);

            var live = await FunctionConcurrency.TryEnterAsync(_db!, _functionId, "run_live", 2);
            live.Should().NotBeNull();

            // The expired member is swept, so only the live one is counted.
            (await FunctionConcurrency.HeldAsync(_db!, _functionId)).Should().Be(1);

            // And releasing the live slot does not resurrect the crashed one.
            await live!.DisposeAsync();
            (await FunctionConcurrency.HeldAsync(_db!, _functionId)).Should().Be(0);
        }

        [SkippableFact]
        public async Task The_limit_is_clamped_to_the_platform_ceiling()
        {
            Skip.If(Unavailable, "no Redis available");

            var held = new List<FunctionConcurrency>();
            for (var i = 0; i < 10; i++)
            {
                var slot = await FunctionConcurrency.TryEnterAsync(_db!, _functionId, $"run_{i}", limit: 1000);
                if (slot is null) break;
                held.Add(slot);
            }

            held.Should().HaveCount(Ceilings.MaxFunctionConcurrency, "no caller may exceed the platform ceiling");

            foreach (var slot in held) await slot.DisposeAsync();
        }
    }
}
