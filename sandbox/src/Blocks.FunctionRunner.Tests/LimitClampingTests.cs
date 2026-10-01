using Blocks.FunctionRunner.Contracts;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The ceilings are the platform's promise. These tests exist because a regression here
    /// would not fail loudly — it would quietly hand tenant code more resources than it may
    /// have, which is exactly the kind of bug that is never noticed until it matters.
    /// </summary>
    public class LimitClampingTests
    {
        [Fact]
        public void Defaults_are_the_ceilings_except_timeout()
        {
            var limits = RunLimits.Default;

            limits.CpuMillicores.Should().Be(100);
            limits.MemoryBytes.Should().Be(200L * 1024 * 1024);
            limits.PidLimit.Should().Be(64);
            limits.TmpfsBytes.Should().Be(64L * 1024 * 1024);
            limits.FunctionConcurrency.Should().Be(2);

            // Timeout is the one field whose default is not its ceiling: 10 s, not 60 s.
            limits.TimeoutSeconds.Should().Be(10);
        }

        [Fact]
        public void A_greedy_request_is_clamped_to_the_ceilings()
        {
            var limits = RunLimits.Clamp(
                cpuMillicores: 64_000,
                memoryBytes: 64L * 1024 * 1024 * 1024,
                pidLimit: 100_000,
                tmpfsBytes: 10L * 1024 * 1024 * 1024,
                timeoutSeconds: 86_400,
                functionConcurrency: 500);

            limits.CpuMillicores.Should().Be(Ceilings.CpuMillicores);
            limits.MemoryBytes.Should().Be(Ceilings.MemoryBytes);
            limits.PidLimit.Should().Be(Ceilings.PidLimit);
            limits.TmpfsBytes.Should().Be(Ceilings.TmpfsBytes);
            limits.TimeoutSeconds.Should().Be(Ceilings.TimeoutSeconds);
            limits.FunctionConcurrency.Should().Be(Ceilings.MaxFunctionConcurrency);
        }

        [Fact]
        public void A_modest_request_is_honoured()
        {
            var limits = RunLimits.Clamp(100, 128 * 1024 * 1024, 32, 16 * 1024 * 1024, 30, 3);

            // CPU is not part of the request any more — the fixed allocation is what arrives
            // however modest the ask.
            limits.CpuMillicores.Should().Be(Ceilings.CpuMillicores);
            limits.MemoryBytes.Should().Be(128 * 1024 * 1024);
            limits.PidLimit.Should().Be(32);
            limits.TimeoutSeconds.Should().Be(30);
            limits.FunctionConcurrency.Should().Be(3);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(null)]
        public void Absent_or_nonsensical_values_fall_back_rather_than_widening(int? value)
        {
            var limits = RunLimits.Clamp(value, value, value, value, value, value);

            limits.CpuMillicores.Should().Be(Ceilings.CpuMillicores);
            limits.MemoryBytes.Should().Be(Ceilings.MemoryBytes);
            limits.TimeoutSeconds.Should().Be(Ceilings.DefaultTimeoutSeconds);
            limits.FunctionConcurrency.Should().Be(Ceilings.DefaultFunctionConcurrency);
        }

        [Fact]
        public void Swap_always_equals_memory_so_a_sandbox_can_never_swap()
        {
            RunLimits.Clamp(null, 128 * 1024 * 1024, null, null, null, null)
                .MemorySwapBytes.Should().Be(128 * 1024 * 1024);
            RunLimits.Default.MemorySwapBytes.Should().Be(RunLimits.Default.MemoryBytes);
        }

        [Fact]
        public void Nanocpus_convert_correctly_for_docker()
        {
            RunLimits.Default.NanoCpus.Should().Be(100_000_000);
        }

        [Fact]
        public void Heap_ceiling_sits_below_the_memory_ceiling()
        {
            var limits = RunLimits.Default;
            limits.MaxOldSpaceMb.Should().Be(150);
            (limits.MaxOldSpaceMb * 1024L * 1024).Should().BeLessThan(limits.MemoryBytes);
        }

        [Theory]
        [InlineData(1L)]
        [InlineData(4L * 1024 * 1024)]           // under Docker's own ~6 MB minimum
        [InlineData((32L * 1024 * 1024) - 1)]
        public void Memory_below_the_floor_is_raised_to_it_so_the_sandbox_can_start(long requested)
        {
            // A few bytes used to clamp "successfully" and then fail at container create.
            var limits = RunLimits.Clamp(null, requested, null, null, null, null);

            limits.MemoryBytes.Should().Be(Ceilings.MinMemoryBytes);
            limits.MemorySwapBytes.Should().Be(Ceilings.MinMemoryBytes);
            limits.MaxOldSpaceMb.Should().BePositive("V8 must still get a heap to start with");
        }

        [Fact]
        public void Memory_at_the_floor_is_honoured()
        {
            RunLimits.Clamp(null, Ceilings.MinMemoryBytes, null, null, null, null)
                .MemoryBytes.Should().Be(Ceilings.MinMemoryBytes);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(8)]
        [InlineData(31)]
        public void Pids_below_the_floor_are_raised_to_it(int requested)
        {
            // Under runsc the limit counts gVisor's own host processes; below the floor the
            // sandbox is never created at all.
            RunLimits.Clamp(null, null, requested, null, null, null)
                .PidLimit.Should().Be(Ceilings.MinPidLimit);
        }

        [Fact]
        public void The_floors_sit_below_the_ceilings()
        {
            Ceilings.MinMemoryBytes.Should().BeLessThan(Ceilings.MemoryBytes);
            Ceilings.MinPidLimit.Should().BeLessThanOrEqualTo(Ceilings.PidLimit);
        }

        [Fact]
        public void Concurrency_below_the_floor_is_raised_not_zeroed()
        {
            RunLimits.Clamp(null, null, null, null, null, 1).FunctionConcurrency.Should().Be(1);
        }
    }
}
