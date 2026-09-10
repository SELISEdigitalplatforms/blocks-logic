using System.Globalization;
using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Retention for the streams. The assertions worth having here are about <i>how</i> the trim
    /// is done, not that it happens: trimming by length (<c>MAXLEN</c>) would ignore the pending
    /// list and could evict an entry a consumer has not finished with, so these pin that the
    /// sweep trims by minimum id — an age boundary held above the payload TTL, where an entry has
    /// nothing left to act on anyway.
    /// </summary>
    public class FunctionStreamTrimmerTests
    {
        private readonly Mock<IDatabase> _database = new(MockBehavior.Loose);
        private readonly List<(string Stream, long MinId)> _trims = [];

        public FunctionStreamTrimmerTests()
        {
            _database
                .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            _database
                .Setup(d => d.StreamTrimByMinIdAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<bool>(),
                    It.IsAny<long?>(), It.IsAny<StreamTrimMode>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey key, RedisValue minId, bool _, long? __, StreamTrimMode ___, CommandFlags ____) =>
                {
                    _trims.Add((key.ToString(), long.Parse(minId.ToString(), CultureInfo.InvariantCulture)));
                    return Task.FromResult(1L);
                });
        }

        private FunctionStreamTrimmer Trimmer(params (string Key, string Value)[] settings)
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_database.Object);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build();

            return new FunctionStreamTrimmer(cache.Object, configuration, NullLogger<FunctionStreamTrimmer>.Instance);
        }

        private long MinIdFor(string stream) => _trims.Single(t => t.Stream == stream).MinId;

        private static double HoursBack(long minIdMs) =>
            (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(minIdMs)).TotalHours;

        [Fact]
        public async Task Trims_every_stream_in_the_contract()
        {
            await Trimmer().SweepOnceAsync(CancellationToken.None);

            _trims.Select(t => t.Stream).Should().BeEquivalentTo([
                FunctionQueueKeys.RunsStream,
                FunctionQueueKeys.ResultsStream,
                FunctionQueueKeys.BuildsStream,
                FunctionQueueKeys.BuildResultsStream,
                FunctionQueueKeys.DeadStream,
                FunctionQueueKeys.DeadResultsStream,
            ]);
        }

        [Fact]
        public async Task Trims_by_minimum_id_never_by_length()
        {
            await Trimmer().SweepOnceAsync(CancellationToken.None);

            // MAXLEN trimming ignores the pending list; using it here could drop work.
            _database.Verify(
                d => d.StreamTrimAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<bool>(),
                    It.IsAny<long?>(), It.IsAny<StreamTrimMode>(), It.IsAny<CommandFlags>()),
                Times.Never);
            _trims.Should().NotBeEmpty();
        }

        [Fact]
        public async Task Working_streams_are_kept_longer_than_the_payload_TTL()
        {
            await Trimmer().SweepOnceAsync(CancellationToken.None);

            // The guarantee that makes age-based trimming safe: anything trimmed is older than
            // function:run:{id}, so there is no envelope left to reclaim it with.
            HoursBack(MinIdFor(FunctionQueueKeys.RunsStream))
                .Should().BeGreaterThan(FunctionQueueKeys.RunTtl.TotalHours);
        }

        [Fact]
        public async Task Dead_letters_outlive_the_working_streams()
        {
            await Trimmer().SweepOnceAsync(CancellationToken.None);

            HoursBack(MinIdFor(FunctionQueueKeys.DeadStream))
                .Should().BeGreaterThan(HoursBack(MinIdFor(FunctionQueueKeys.RunsStream)));
        }

        [Fact]
        public async Task Retention_is_configurable_per_stream_class()
        {
            await Trimmer(
                ("Functions:StreamRetentionHours", "72"),
                ("Functions:DeadStreamRetentionHours", "240")).SweepOnceAsync(CancellationToken.None);

            HoursBack(MinIdFor(FunctionQueueKeys.RunsStream)).Should().BeApproximately(72, 1);
            HoursBack(MinIdFor(FunctionQueueKeys.DeadStream)).Should().BeApproximately(240, 1);
        }

        [Fact]
        public async Task A_zero_or_negative_configured_retention_falls_back_to_the_default()
        {
            // Otherwise a typo in configuration would trim the streams to nothing.
            await Trimmer(("Functions:StreamRetentionHours", "0")).SweepOnceAsync(CancellationToken.None);

            HoursBack(MinIdFor(FunctionQueueKeys.RunsStream))
                .Should().BeGreaterThan(FunctionQueueKeys.RunTtl.TotalHours);
        }

        [Fact]
        public async Task A_retention_below_the_payload_TTL_is_refused_not_obeyed()
        {
            // Obeying it would trim entries whose envelope is still alive — live work. The floor
            // is the whole reason age-based trimming is safe, so configuration cannot lower it.
            await Trimmer(("Functions:StreamRetentionHours", "1")).SweepOnceAsync(CancellationToken.None);

            HoursBack(MinIdFor(FunctionQueueKeys.RunsStream))
                .Should().BeGreaterThan(FunctionQueueKeys.RunTtl.TotalHours);
        }

        [Fact]
        public async Task A_stream_that_does_not_exist_yet_is_skipped()
        {
            _database
                .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);

            var removed = await Trimmer().SweepOnceAsync(CancellationToken.None);

            removed.Should().Be(0);
            _trims.Should().BeEmpty();
        }
    }
}
