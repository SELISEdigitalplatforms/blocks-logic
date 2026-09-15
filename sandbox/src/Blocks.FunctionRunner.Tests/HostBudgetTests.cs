using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The admission controller sizes itself from the host, so what has to be pinned down is the
    /// control loop: that it grows only into real demand, that it gives ground quickly when
    /// memory complains, and that neither behaviour can talk it past the arithmetic bound. The
    /// host is faked rather than read, because the interesting states — a stalling kernel, a
    /// nearly-full machine — are not ones a test run can arrange for itself.
    /// </summary>
    public class HostBudgetTests
    {
        private const long Mb = 1024 * 1024;

        /// <summary>A host that reports exactly what a test tells it to.</summary>
        private sealed class FakeHostSignals : IHostSignals
        {
            public double Cores { get; set; } = 4;

            public long TotalMemoryBytes { get; set; } = 8192 * Mb;

            public double CpuStalledPercent { get; set; }

            public double MemoryStalledPercent { get; set; }

            public double MemoryFullStalledPercent { get; set; }

            public long AvailableMemoryBytes { get; set; } = 8192 * Mb;

            /// <summary>How many times the controller has actually looked at the host.</summary>
            public int SampleCount { get; private set; }

            public HostSignalSample Sample()
            {
                SampleCount++;
                return new HostSignalSample(
                    CpuStalledPercent, MemoryStalledPercent, MemoryFullStalledPercent, AvailableMemoryBytes);
            }
        }

        /// <summary>
        /// A hand-rolled clock. The project has no mocking library and no FakeTimeProvider
        /// package; TimeProvider only needs two members overridden for GetElapsedTime to work.
        /// </summary>
        private sealed class StubTime : TimeProvider
        {
            private long _ticks = TimeSpan.TicksPerHour;

            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override long GetTimestamp() => _ticks;

            /// <summary>Moves past the sample interval, so the next decision re-reads the host.</summary>
            public void PastTheSampleInterval() => _ticks += HostBudget.SampleInterval.Ticks * 2;
        }

        private static HostBudget Build(FakeHostSignals signals, StubTime time, int? pinned = null)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                MaxActiveSandboxes = pinned,
                ReservedHostMemoryMb = 2048,
            });

            return new HostBudget(
                options, signals, new SandboxFootprint(), NullLogger<HostBudget>.Instance, time);
        }

        /// <summary>
        /// A tenth of a core per sandbox is the fixed allocation, so 0.2 cores is a two-slot host
        /// — small enough that the loop's arithmetic is readable in the assertions.
        /// </summary>
        private static FakeHostSignals TwoSlotHost() => new() { Cores = 0.2 };

        [Fact]
        public void Starts_at_the_smaller_of_what_cpu_and_memory_allow()
        {
            // 4 cores is 40 sandboxes' worth of CPU, but only 3 fit in memory once the host's
            // own 2 GB is withheld, so memory is what the controller must start from.
            var signals = new FakeHostSignals { Cores = 4, TotalMemoryBytes = 2048 * Mb + 3 * Ceilings.MemoryBytes };

            Build(signals, new StubTime()).Capacity.Should().Be(3);
        }

        [Fact]
        public void Does_not_grow_while_the_host_still_has_a_free_slot()
        {
            var signals = TwoSlotHost();
            var time = new StubTime();
            var budget = Build(signals, time);

            budget.Capacity.Should().Be(2);

            using var first = budget.TryReserve(Ceilings.MemoryBytes);
            first.Should().NotBeNull();

            // One of two slots is in use and the kernel reports nothing stalling. There is no
            // demand to answer, so another slot would be a number nobody asked for.
            time.PastTheSampleInterval();
            using var second = budget.TryReserve(Ceilings.MemoryBytes);

            second.Should().NotBeNull();
            budget.Capacity.Should().Be(2);
        }

        [Fact]
        public void Grows_one_slot_when_the_host_is_full_and_cpu_is_not_stalling()
        {
            var signals = TwoSlotHost();
            var time = new StubTime();
            var budget = Build(signals, time);

            using var first = budget.TryReserve(Ceilings.MemoryBytes);
            using var second = budget.TryReserve(Ceilings.MemoryBytes);
            budget.Active.Should().Be(2);

            signals.CpuStalledPercent = HostBudget.CpuStallGrowBelowPercent - 1;
            time.PastTheSampleInterval();
            using var third = budget.TryReserve(Ceilings.MemoryBytes);

            budget.Capacity.Should().Be(3);
            third.Should().NotBeNull("the slot the controller just opened is the one this caller takes");
        }

        [Fact]
        public void Does_not_grow_into_a_full_host_that_is_already_stalling_on_cpu()
        {
            var signals = TwoSlotHost();
            var time = new StubTime();
            var budget = Build(signals, time);

            using var first = budget.TryReserve(Ceilings.MemoryBytes);
            using var second = budget.TryReserve(Ceilings.MemoryBytes);

            signals.CpuStalledPercent = HostBudget.CpuStallShrinkAbovePercent + 1;
            time.PastTheSampleInterval();
            budget.TryReserve(Ceilings.MemoryBytes).Should().BeNull();

            budget.Capacity.Should().Be(1, "past the shrink setpoint the host gives a slot back");
        }

        [Fact]
        public void Shrinks_by_a_quarter_the_moment_memory_stalls_completely()
        {
            // 16 slots, so a quarter is visibly more than the one-at-a-time CPU response.
            var signals = new FakeHostSignals { Cores = 1.6 };
            var time = new StubTime();
            var budget = Build(signals, time);

            budget.Capacity.Should().Be(16);

            signals.MemoryFullStalledPercent = 0.5;
            time.PastTheSampleInterval();
            budget.TryReserve(Ceilings.MemoryBytes)?.Dispose();

            budget.Capacity.Should().Be(12);
        }

        [Fact]
        public void Shrinks_when_memory_stalls_partially_too()
        {
            var signals = new FakeHostSignals { Cores = 1.6 };
            var time = new StubTime();
            var budget = Build(signals, time);

            signals.MemoryStalledPercent = HostBudget.MemoryStallShrinkAbovePercent + 0.5;
            time.PastTheSampleInterval();
            budget.TryReserve(Ceilings.MemoryBytes)?.Dispose();

            budget.Capacity.Should().Be(12);
        }

        [Fact]
        public void Never_grows_past_what_memory_can_hold_however_calm_the_host_looks()
        {
            // Room for three sandboxes and 40 cores' worth of CPU: PSI will never object, so the
            // arithmetic bound is the only thing standing between this and a host that OOMs.
            var signals = new FakeHostSignals { Cores = 4, TotalMemoryBytes = 2048 * Mb + 3 * Ceilings.MemoryBytes };
            var time = new StubTime();
            var budget = Build(signals, time);

            var held = new List<IDisposable>();
            try
            {
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    time.PastTheSampleInterval();
                    if (budget.TryReserve(Ceilings.MemoryBytes) is { } reservation) held.Add(reservation);
                }

                held.Should().HaveCount(3);
                budget.Capacity.Should().Be(3);
            }
            finally
            {
                foreach (var reservation in held) reservation.Dispose();
            }
        }

        [Fact]
        public void Refuses_when_the_kernel_says_the_host_is_nearly_out_of_memory()
        {
            var signals = new FakeHostSignals { Cores = 4 };
            var time = new StubTime();
            var budget = Build(signals, time);

            // Committed memory is nil and slots are plentiful; only MemAvailable objects. It has
            // to, because it is the one figure that also covers what this runner never promised.
            signals.AvailableMemoryBytes = 2048 * Mb + Ceilings.MemoryBytes - 1;
            budget.TryReserve(Ceilings.MemoryBytes).Should().BeNull();

            signals.AvailableMemoryBytes = 8192 * Mb;
            time.PastTheSampleInterval();
            using var granted = budget.TryReserve(Ceilings.MemoryBytes);
            granted.Should().NotBeNull();
        }

        [Fact]
        public void An_unreadable_MemAvailable_does_not_read_as_an_empty_host()
        {
            var signals = new FakeHostSignals { Cores = 4, AvailableMemoryBytes = long.MaxValue };
            using var reservation = Build(signals, new StubTime()).TryReserve(Ceilings.MemoryBytes);

            reservation.Should().NotBeNull("an unknown figure must not wedge admission shut");
        }

        [Fact]
        public void A_pinned_count_disables_the_loop_in_both_directions()
        {
            var signals = new FakeHostSignals { Cores = 4 };
            var time = new StubTime();
            var budget = Build(signals, time, pinned: 7);

            budget.Capacity.Should().Be(7);

            var held = new List<IDisposable>();
            try
            {
                for (var attempt = 0; attempt < 7; attempt++)
                {
                    time.PastTheSampleInterval();
                    var reservation = budget.TryReserve(Ceilings.MemoryBytes);
                    reservation.Should().NotBeNull();
                    held.Add(reservation!);
                }

                // The host is now full and perfectly calm — exactly the state that would have
                // opened another slot had the controller been running.
                signals.CpuStalledPercent = 0;
                time.PastTheSampleInterval();
                budget.TryReserve(Ceilings.MemoryBytes).Should().BeNull();
                budget.Capacity.Should().Be(7);

                // And memory stalling outright, which would have taken a quarter of them away.
                signals.MemoryFullStalledPercent = 50;
                time.PastTheSampleInterval();
                budget.TryReserve(Ceilings.MemoryBytes).Should().BeNull();
                budget.Capacity.Should().Be(7);
            }
            finally
            {
                foreach (var reservation in held) reservation.Dispose();
            }
        }

        [Fact]
        public void A_pinned_count_still_reads_the_host_so_the_memory_floor_holds()
        {
            var signals = new FakeHostSignals { Cores = 4, AvailableMemoryBytes = 2048 * Mb + Ceilings.MemoryBytes - 1 };
            var budget = Build(signals, new StubTime(), pinned: 7);

            // Pinning hands the slot count to the operator, not the safety check. Skipping the
            // sample here would leave MemAvailable at zero and refuse every run on the host.
            budget.TryReserve(Ceilings.MemoryBytes).Should().BeNull();
            signals.SampleCount.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Does_not_re_read_the_host_more_than_once_per_sample_interval()
        {
            var signals = TwoSlotHost();
            var time = new StubTime();
            var budget = Build(signals, time);

            for (var attempt = 0; attempt < 10; attempt++) budget.TryReserve(Ceilings.MemoryBytes)?.Dispose();

            signals.SampleCount.Should().Be(1);

            time.PastTheSampleInterval();
            budget.TryReserve(Ceilings.MemoryBytes)?.Dispose();

            signals.SampleCount.Should().Be(2);
        }

        [Fact]
        public void Releasing_a_reservation_returns_its_slot_and_its_memory()
        {
            var signals = TwoSlotHost();
            var budget = Build(signals, new StubTime());

            var reservation = budget.TryReserve(Ceilings.MemoryBytes);
            budget.Active.Should().Be(1);
            budget.CommittedMemoryBytes.Should().Be(Ceilings.MemoryBytes);

            reservation!.Dispose();
            budget.Active.Should().Be(0);
            budget.CommittedMemoryBytes.Should().Be(0);

            // Disposing twice must not credit the host with memory it never got back.
            reservation.Dispose();
            budget.Active.Should().Be(0);
            budget.CommittedMemoryBytes.Should().Be(0);
        }

        [Fact]
        public void Reserves_the_measured_cost_rather_than_the_limit_once_runs_have_been_seen()
        {
            var signals = TwoSlotHost();
            var budget = Build(signals, new StubTime());

            for (var sample = 0; sample < SandboxFootprint.MinimumSamples; sample++) budget.Observe(40 * Mb);

            using var reservation = budget.TryReserve(Ceilings.MemoryBytes);

            reservation.Should().NotBeNull();
            budget.CommittedMemoryBytes.Should().Be(40 * Mb, "the host has measured what a sandbox costs it");
        }
    }
}
