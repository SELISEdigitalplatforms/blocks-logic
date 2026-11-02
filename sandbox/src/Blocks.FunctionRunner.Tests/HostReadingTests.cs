using Blocks.FunctionRunner.Health;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>The kernel readings and the thresholds applied to them, as the recorder and police share them.</summary>
    public sealed class HostReadingTests
    {
        [Fact]
        public void Meminfo_is_parsed_with_cache_as_cached_plus_reclaimable_slab()
        {
            var info = MemInfo.Parse(
            [
                "MemTotal:       16000000 kB",
                "MemAvailable:    4000000 kB",
                "Cached:          3000000 kB",
                "SReclaimable:     500000 kB",
                "AnonPages:       6000000 kB",
                "SwapTotal:       4000000 kB",
                "SwapFree:        1000000 kB",
                "garbage line",
            ]);

            info.Should().Be(new MemInfo(16_000_000, 4_000_000, 6_000_000, 3_500_000, 4_000_000, 1_000_000));
        }

        [Fact]
        public void Cpu_percent_is_the_busy_share_between_two_readings()
        {
            var before = CpuTimes.Parse("cpu  100 0 100 700 100 0 0 0 0 0")!.Value;
            var after = CpuTimes.Parse("cpu  200 0 200 750 150 0 0 0 0 0")!.Value;

            // 300 jiffies passed, 100 of them idle: 200 busy.
            CpuTimes.Percent(before, after).Should().BeApproximately(66.67, 0.01);
            CpuTimes.Percent(before, before).Should().BeNull();
        }

        [Fact]
        public void A_sample_over_each_limit_is_a_breach_and_a_healthy_one_is_not()
        {
            var bad = new HostStats(1000, 99, 0, 0, 1000, 801, 91, 1, 86);
            var good = new HostStats(1000, 101, 0, 0, 1000, 800, 90, 1, 85);

            HealthRules.HostBreaches(bad).Select(b => b.Key).Should().Equal("memory", "swap", "cpu", "disk");
            HealthRules.HostBreaches(good).Should().BeEmpty();
        }

        [Fact]
        public void Unknown_readings_and_no_swap_are_never_a_breach()
        {
            var unknown = new HostStats(null, null, null, null, 0, 0, null, null, null);

            HealthRules.HostBreaches(unknown).Should().BeEmpty();
        }
    }
}
