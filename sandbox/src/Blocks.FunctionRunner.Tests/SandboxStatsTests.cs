using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet.Models;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Docker's stats stream brackets a run with zero samples — one when the stream attaches
    /// before the container has done anything, one or more after it exits and its cgroup is
    /// gone. Taking the latest sample therefore reported 0 ms of CPU for every run that ended
    /// normally. These tests pin the shape of a real stream, zero bookends and all.
    /// </summary>
    public class SandboxStatsTests
    {
        private static ContainerStatsResponse Sample(ulong? cpuNs, ulong? memoryBytes) => new()
        {
            CPUStats = cpuNs is { } cpu
                ? new CPUStats { CPUUsage = new CPUUsage { TotalUsage = cpu } }
                : null,
            MemoryStats = memoryBytes is { } memory
                ? new MemoryStats { Usage = memory }
                : null,
        };

        /// <summary>The sequence observed from a container that burns CPU and then exits.</summary>
        private static void FeedRealStream(SandboxStatsAccumulator stats)
        {
            stats.Add(Sample(0, null));                    // attached before the container moved
            stats.Add(Sample(876_869_000, 606_208));
            stats.Add(Sample(1_880_543_000, 581_632));
            stats.Add(Sample(8_896_070_000, 372_736));
            stats.Add(Sample(0, null));                    // container gone, cgroup with it
            stats.Add(Sample(0, null));
        }

        [Fact]
        public void The_zero_sample_after_the_container_exits_does_not_erase_the_cpu_total()
        {
            var stats = new SandboxStatsAccumulator();
            FeedRealStream(stats);

            // 8.89607s of CPU, not the 0 ms the last sample claims.
            stats.CpuUsageMs.Should().Be(8897);
        }

        [Fact]
        public void Peak_memory_survives_the_same_stream()
        {
            var stats = new SandboxStatsAccumulator();
            FeedRealStream(stats);

            stats.PeakMemoryBytes.Should().Be(606_208);
        }

        [Fact]
        public void Cpu_time_is_the_high_water_mark_of_a_counter_that_only_climbs()
        {
            var stats = new SandboxStatsAccumulator();
            stats.Add(Sample(5_000_000_000, 1024));
            stats.Add(Sample(1_000_000_000, 1024)); // a counter going backwards is not a reading

            stats.CpuUsageMs.Should().Be(5000);
        }

        [Fact]
        public void A_run_that_ends_before_any_sample_arrives_reports_nothing_at_all()
        {
            var stats = new SandboxStatsAccumulator();

            stats.CpuUsageMs.Should().BeNull();
            stats.PeakMemoryBytes.Should().BeNull();
        }

        [Fact]
        public void Zero_readings_alone_are_not_a_measurement()
        {
            // Both bookends and nothing between: the container came and went between samples.
            var stats = new SandboxStatsAccumulator();
            stats.Add(Sample(0, null));
            stats.Add(Sample(0, null));

            // Null, not 0 — "never measured" must stay distinguishable from "used no CPU".
            stats.CpuUsageMs.Should().BeNull();
            stats.PeakMemoryBytes.Should().BeNull();
        }

        [Fact]
        public void Sub_millisecond_cpu_time_rounds_up_rather_than_down_to_the_broken_reading()
        {
            var stats = new SandboxStatsAccumulator();
            stats.Add(Sample(400_000, 1024)); // 0.4ms

            stats.CpuUsageMs.Should().Be(1);
        }

        [Fact]
        public void A_sample_missing_one_half_leaves_the_other_alone()
        {
            var stats = new SandboxStatsAccumulator();
            stats.Add(Sample(2_000_000_000, 4096));
            stats.Add(Sample(null, 8192));
            stats.Add(Sample(3_000_000_000, null));

            stats.CpuUsageMs.Should().Be(3000);
            stats.PeakMemoryBytes.Should().Be(8192);
        }

        [Fact]
        public void A_counter_past_long_max_is_discarded_instead_of_reported_as_negative()
        {
            // The unchecked cast of a ulong that high yields a negative long; a nonsense reading
            // must not outrank a real one, nor become a negative duration in the run record.
            var stats = new SandboxStatsAccumulator();
            stats.Add(Sample(7_000_000_000, 2048));
            stats.Add(Sample(ulong.MaxValue, ulong.MaxValue));

            stats.CpuUsageMs.Should().Be(7000);
            stats.PeakMemoryBytes.Should().Be(2048);
        }

        [Fact]
        public void Overlapping_callbacks_cannot_lose_an_update()
        {
            // Progress<T> posts to the thread pool, so samples can land concurrently.
            var stats = new SandboxStatsAccumulator();
            Parallel.For(1, 501, i => stats.Add(Sample((ulong)i * 1_000_000, (ulong)i * 1024)));

            stats.CpuUsageMs.Should().Be(500);
            stats.PeakMemoryBytes.Should().Be(500 * 1024);
        }
    }
}
