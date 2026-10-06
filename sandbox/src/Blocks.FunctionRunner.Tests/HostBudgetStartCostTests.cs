using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Starting a container costs the host ~0.8 CPU-seconds outside every sandbox's quota
    /// (sandbox/REUSE.md). Admission must plan for it, charge it on every start, and limit the
    /// start rate — and a warm sandbox must hold its memory while it lives but a slot only while
    /// it serves a call.
    /// </summary>
    public class HostBudgetStartCostTests
    {
        private static HostBudget Build(CalmHost host, ManualTime time, int startCost = 800, double perCore = 1.0, int? pinned = null)
            => new(
                Microsoft.Extensions.Options.Options.Create(new RunnerOptions
                {
                    StartCostCpuMs = startCost,
                    StartsPerSecondPerCore = perCore,
                    MaxActiveSandboxes = pinned,
                    ReservedHostMemoryMb = 2048,
                }),
                host, new SandboxFootprint(), NullLogger<HostBudget>.Instance, time);

        [Fact]
        public void Initial_slots_leave_room_for_the_start_overhead()
        {
            // 8 cores: 80 slots when every millicore was sandbox quota; with 800 of each 1000
            // set aside for starts, 8 × 200 / 100 = 16.
            Build(new CalmHost { Cores = 8 }, new ManualTime()).Capacity.Should().Be(16);
        }

        [Fact]
        public void Without_a_start_cost_the_initial_slots_are_what_they_always_were()
        {
            Build(new CalmHost { Cores = 8 }, new ManualTime(), startCost: 0).Capacity.Should().Be(80);
        }

        [Fact]
        public void Starts_are_rate_limited_and_refill_with_time()
        {
            var time = new ManualTime();
            // 2 cores × 1 start/s → a burst of two, then one more every half second.
            var budget = Build(new CalmHost { Cores = 2 }, time, pinned: 100);

            using var a = budget.TryReserve(Ceilings.MemoryBytes);
            using var b = budget.TryReserve(Ceilings.MemoryBytes);
            using var c = budget.TryReserve(Ceilings.MemoryBytes);

            a.Should().NotBeNull();
            b.Should().NotBeNull();
            c.Should().BeNull("the start bucket is empty, though slots and memory are free");
            budget.Active.Should().Be(2, "a refused start takes no slot");

            time.Advance(TimeSpan.FromMilliseconds(500));
            using var d = budget.TryReserve(Ceilings.MemoryBytes);
            d.Should().NotBeNull();
        }

        [Fact]
        public void A_warm_sandbox_holds_memory_and_a_start_but_no_slot()
        {
            var budget = Build(new CalmHost(), new ManualTime(), pinned: 10);

            var warm = budget.TryReserveWarm(Ceilings.MemoryBytes);

            warm.Should().NotBeNull();
            budget.Warm.Should().Be(1);
            budget.Active.Should().Be(0, "a paused sandbox uses no CPU");
            budget.CommittedMemoryBytes.Should().Be(Ceilings.MemoryBytes);

            using (var call = budget.TryReserveSlot())
            {
                call.Should().NotBeNull();
                budget.Active.Should().Be(1, "a busy warm sandbox is a CPU user");
                budget.CommittedMemoryBytes.Should().Be(Ceilings.MemoryBytes, "the call charges no memory again");
            }

            budget.Active.Should().Be(0);
            warm!.Dispose();
            warm.Dispose();
            budget.Warm.Should().Be(0);
            budget.CommittedMemoryBytes.Should().Be(0, "released exactly once");
        }

        [Fact]
        public void A_warm_start_is_refused_when_memory_is_spoken_for()
        {
            // 2 GB reserved for the host, 2 GB + 256 MB total: room for two 128 MB sandboxes.
            var host = new CalmHost { TotalMemoryBytes = (2048L + 256) * 1024 * 1024, AvailableMemoryBytes = 64L * 1024 * 1024 * 1024 };
            var budget = Build(host, new ManualTime(), startCost: 0, pinned: 10);

            using var one = budget.TryReserveWarm(Ceilings.MemoryBytes);
            using var two = budget.TryReserveWarm(Ceilings.MemoryBytes);
            using var three = budget.TryReserveWarm(Ceilings.MemoryBytes);

            one.Should().NotBeNull();
            two.Should().NotBeNull();
            three.Should().BeNull();
            budget.TryReserve(Ceilings.MemoryBytes).Should().BeNull("warm sandboxes hold their memory while idle");
        }

        [Fact]
        public void A_slot_alone_respects_the_slot_count()
        {
            var budget = Build(new CalmHost(), new ManualTime(), pinned: 1);

            using var first = budget.TryReserveSlot();
            using var second = budget.TryReserveSlot();

            first.Should().NotBeNull();
            second.Should().BeNull();
        }
    }
}
