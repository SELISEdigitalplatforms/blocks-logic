using Blocks.FunctionRunner.Health;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>A clock a test can move.</summary>
    internal sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset start)
        {
            _now = start;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>
    /// What stays on disk. The recorder writes every 10 seconds for ever, so a retention mistake
    /// is either a full disk (too much kept) or an empty <c>fnctl stats --hour</c> (too little).
    /// </summary>
    public sealed class MetricsStoreTests : IDisposable
    {
        private static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "fn-metrics-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* temp dir, best effort */ }
            GC.SuppressFinalize(this);
        }

        private static MetricsSample SampleAt(DateTimeOffset ts) => new(
            ts, "r1", true, new WarmSample(0, 0, 0), new Dictionary<string, StreamSample>(),
            new HostStats(null, null, null, null, null, null, null, null, null), new DockerStats(null, null, null, null));

        private string[] Files() => Directory.GetFiles(_dir).Select(Path.GetFileName).OfType<string>().Order().ToArray();

        [Fact]
        public void Sample_file_is_named_for_its_ten_minute_window()
        {
            MetricsStore.SampleFileName(new DateTimeOffset(2026, 11, 2, 12, 39, 59, TimeSpan.Zero))
                .Should().Be("samples-202611021230.jsonl");
        }

        [Fact]
        public void Sample_files_are_kept_for_an_hour_after_their_window_ends_and_not_longer()
        {
            var clock = new ManualClock(Noon);
            var store = new MetricsStore(_dir, clock);
            // Window 10:50-11:00 ended exactly 60 min ago (kept); 10:40-10:50 ended 70 min ago (deleted).
            store.AppendSample(SampleAt(Noon.AddMinutes(-80)));
            store.AppendSample(SampleAt(Noon.AddMinutes(-70)));
            store.AppendSample(SampleAt(Noon.AddMinutes(-1)));

            store.PruneSamples();

            Files().Should().Equal("samples-202611021050.jsonl", "samples-202611021150.jsonl");
        }

        [Fact]
        public void Event_and_police_files_are_kept_seven_days()
        {
            var clock = new ManualClock(Noon);
            var store = new MetricsStore(_dir, clock);
            var old = new MetricsEvent(Noon.AddDays(-9), "k", "Warning", "old");
            var recent = new MetricsEvent(Noon.AddDays(-6), "k", "Warning", "recent");
            store.AppendEvent(old);
            store.AppendEvent(recent);
            store.AppendPolice(old);

            store.PruneDaily(MetricsStore.EventsPrefix);
            store.PruneDaily(MetricsStore.PolicePrefix);

            Files().Should().Equal("events-20261027.jsonl");
        }

        [Fact]
        public void Prune_leaves_files_it_does_not_own()
        {
            var store = new MetricsStore(_dir, new ManualClock(Noon));
            store.WriteGcLast(3);
            File.WriteAllText(Path.Combine(_dir, "samples-notadate.jsonl"), "x");

            store.PruneSamples();
            store.PruneDaily(MetricsStore.EventsPrefix);

            Files().Should().Equal("gc-last.json", "samples-notadate.jsonl");
        }

        [Fact]
        public void A_half_written_last_line_does_not_hide_the_newest_sample()
        {
            var store = new MetricsStore(_dir, new ManualClock(Noon));
            store.AppendSample(SampleAt(Noon.AddSeconds(-20)));
            File.AppendAllText(Path.Combine(_dir, MetricsStore.SampleFileName(Noon)), "{\"ts\":\"2026-11-0");

            store.ReadLatestSample()!.Ts.Should().Be(Noon.AddSeconds(-20));
        }

        [Fact]
        public void A_write_that_fails_is_reported_not_thrown()
        {
            var blocked = Path.Combine(_dir, "a-file");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(blocked, "x");
            var store = new MetricsStore(Path.Combine(blocked, "sub"), new ManualClock(Noon));
            string? reason = null;
            store.WriteFailed += r => reason = r;

            store.AppendSample(SampleAt(Noon));

            reason.Should().NotBeNull();
        }
    }
}
