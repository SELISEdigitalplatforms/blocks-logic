using Blocks.FunctionRunner.Redis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// FN-4: a run loop reads several new entries in one round trip, and a fresh read is not asked
    /// its delivery count, while a reclaimed entry still is — so the poison-entry budget keeps
    /// working. Against a real Redis (skipping cleanly without one; set TEST_REDIS), on a stream of
    /// its own.
    /// </summary>
    public sealed class GroupConsumerTests : IAsyncLifetime
    {
        private readonly string _stream = $"test:group-consumer:{Guid.NewGuid():N}";
        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;

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
            if (_db is not null) await _db.KeyDeleteAsync(_stream);
            if (_redis is not null) await _redis.DisposeAsync();
        }

        private GroupConsumer Consumer(string name) =>
            new(_db!, NullLogger.Instance, _stream, "runners", name);

        private async Task AddAsync(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await _db!.StreamAddAsync(_stream, [new NameValueEntry("runId", $"run_{i}")]);
            }
        }

        [Fact]
        public async Task Reads_up_to_the_asked_count_in_one_call_and_marks_them_fresh()
        {
            if (_db is null) return;
            var consumer = Consumer("a");
            await consumer.EnsureGroupAsync();
            await AddAsync(5);

            var first = await consumer.ReadNewAsync(3, CancellationToken.None);
            var rest = await consumer.ReadNewAsync(16, CancellationToken.None);

            first.Should().HaveCount(3);
            rest.Should().HaveCount(2, "only what is on the stream, never more");
            first.Concat(rest).Should().OnlyContain(e => e.Fresh);
        }

        [Fact]
        public async Task A_fresh_read_counts_as_delivery_one()
        {
            if (_db is null) return;
            var consumer = Consumer("a");
            await consumer.EnsureGroupAsync();
            await AddAsync(1);

            var entry = (await consumer.ReadNewAsync(1, CancellationToken.None)).Single();

            (await consumer.DeliveryCountAsync(entry)).Should().Be(1);
            (await consumer.DeliveryCountAsync(entry.Id)).Should().Be(1, "the shortcut agrees with Redis");
        }

        [Fact]
        public async Task A_reclaimed_entry_is_not_fresh_and_its_real_delivery_count_is_read()
        {
            if (_db is null) return;
            var dead = Consumer("dead-runner");
            var alive = Consumer("alive-runner");
            await dead.EnsureGroupAsync();
            await AddAsync(1);
            await dead.ReadNewAsync(1, CancellationToken.None);

            var reclaimed = (await alive.ReclaimAbandonedAsync(TimeSpan.Zero, 5)).Single();

            reclaimed.Fresh.Should().BeFalse();
            (await alive.DeliveryCountAsync(reclaimed)).Should().Be(2,
                "a redelivery must keep counting, or a poison entry would circle forever");
        }
    }
}
