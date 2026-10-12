using Blocks.FunctionRunner.Contracts;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The profile is the platform's promise, and this half is where it is actually enforced.
    /// <para>
    /// A regression here would not fail loudly — it would quietly hand tenant code more resources
    /// than it may have, which is the kind of bug nobody notices until it matters. The control plane
    /// applies the same numbers first; this side applies them again so a mistake up there cannot
    /// widen a sandbox.
    /// </para>
    /// </summary>
    public class LimitClampingTests
    {
        [Fact]
        public void The_default_profile_is_the_platform_profile()
        {
            var limits = RunLimits.Default;

            limits.CpuMillicores.Should().Be(Ceilings.CpuMillicores);
            limits.MemoryBytes.Should().Be(Ceilings.MemoryBytes);
            limits.PidLimit.Should().Be(Ceilings.PidLimit);
            limits.TmpfsBytes.Should().Be(Ceilings.TmpfsBytes);
            limits.TimeoutSeconds.Should().Be(Ceilings.TimeoutSeconds);
            limits.FunctionConcurrency.Should().Be(Ceilings.MaxFunctionConcurrency);
        }

        /// <summary>
        /// The whole point: a request cannot move any dimension, in either direction.
        /// <para>
        /// Greedy, modest, absent and nonsensical all produce the same sandbox. A tenant cannot
        /// widen one, and cannot narrow one either — so a slot on a host means the same thing
        /// whoever is running in it, which is what makes capacity something you can plan with.
        /// </para>
        /// </summary>
        [Theory]
        // greedy
        [InlineData(64_000, 64L * 1024 * 1024 * 1024, 100_000, 8L * 1024 * 1024 * 1024, 86_400, 500)]
        // modest
        [InlineData(50, 64L * 1024 * 1024, 16, 8L * 1024 * 1024, 5, 1)]
        // absent
        [InlineData(null, null, null, null, null, null)]
        // nonsensical
        [InlineData(0, 0L, 0, 0L, 0, 0)]
        [InlineData(-1, -1L, -1, -1L, -1, -1)]
        public void Whatever_is_requested_the_sandbox_is_the_same(
            int? cpu, long? memory, int? pids, long? tmpfs, int? timeout, int? concurrency)
        {
            var limits = RunLimits.Clamp(cpu, memory, pids, tmpfs, timeout, concurrency);

            limits.Should().BeEquivalentTo(RunLimits.Default);
        }

        [Fact]
        public void Swap_always_equals_memory_so_a_sandbox_can_never_swap()
        {
            RunLimits.Default.MemorySwapBytes.Should().Be(RunLimits.Default.MemoryBytes);
        }

        [Fact]
        public void Nanocpus_convert_correctly_for_docker()
        {
            RunLimits.Default.NanoCpus.Should().Be(Ceilings.CpuMillicores * 1_000_000L);
        }

        /// <summary>
        /// V8's old space is held under the cgroup so a heap exhaustion throws something the
        /// function can catch, rather than the OOM killer taking the container with no explanation.
        /// </summary>
        [Fact]
        public void Heap_ceiling_sits_below_the_memory_ceiling()
        {
            var limits = RunLimits.Default;

            limits.MaxOldSpaceMb.Should().BeLessThan((int)(limits.MemoryBytes / 1024 / 1024));
            limits.MaxOldSpaceMb.Should().Be(96, "75 % of 128 MB");
        }

        /// <summary>
        /// Pinned as literals as well as compared to the constants, so changing a platform promise
        /// takes a deliberate edit here and not just a number somewhere else. The control plane keeps
        /// its own copy of these and <c>FunctionLimitsTests</c> is what holds the two equal.
        /// </summary>
        [Fact]
        public void The_profile_is_the_documented_one()
        {
            Ceilings.CpuMillicores.Should().Be(100);
            Ceilings.MemoryBytes.Should().Be(128L * 1024 * 1024);
            Ceilings.TimeoutSeconds.Should().Be(30);
            Ceilings.MaxFunctionConcurrency.Should().Be(10);
            Ceilings.PidLimit.Should().Be(64);
            Ceilings.TmpfsBytes.Should().Be(64L * 1024 * 1024);
        }

        /// <summary>
        /// The floors are no longer reachable — nothing may request less — but they still document
        /// what a sandbox needs to start at all, and the fixed profile must sit above every one of
        /// them or no sandbox would start.
        /// </summary>
        [Fact]
        public void The_fixed_profile_clears_every_floor_a_sandbox_needs_to_start()
        {
            Ceilings.MemoryBytes.Should().BeGreaterThan(Ceilings.MinMemoryBytes);
            Ceilings.PidLimit.Should().BeGreaterThan(Ceilings.MinPidLimit);
            Ceilings.MaxFunctionConcurrency.Should().BeGreaterThanOrEqualTo(Ceilings.MinFunctionConcurrency);
        }
    }
}
