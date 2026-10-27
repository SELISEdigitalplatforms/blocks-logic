using Blocks.FunctionRunner.Admission;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The room check turns on two figures read as text out of <c>/proc</c>: CPU in use from
    /// <c>/proc/stat</c> and <c>MemAvailable</c>. The samples are literal copies of what the kernel
    /// writes. A parser that read a busy machine as idle would start sandboxes into a full host; one
    /// that read an idle machine as busy would refuse work on an empty one — the 2026-10-26 failure.
    /// </summary>
    public class ProcHostSignalsTests
    {
        [Fact]
        public void Reads_total_and_idle_cpu_time_from_the_aggregate_line()
        {
            // user nice system idle iowait irq softirq steal guest guest_nice
            var times = ProcHostSignals.ParseCpuLine("cpu  4705 150 1120 16250 520 0 30 5 100 0");

            times.Should().Be(new ProcHostSignals.CpuTimes(4705 + 150 + 1120 + 16250 + 520 + 0 + 30 + 5, 16250 + 520));
        }

        [Fact]
        public void A_per_cpu_line_or_garbage_is_not_read_as_the_machine()
        {
            ProcHostSignals.ParseCpuLine("cpu0 100 0 100 800 0 0 0 0").Should().BeNull();
            ProcHostSignals.ParseCpuLine("cpu  x y z").Should().BeNull();
        }

        [Fact]
        public void Cpu_in_use_is_the_busy_share_between_two_readings()
        {
            var before = new ProcHostSignals.CpuTimes(Total: 1000, Idle: 800);
            var after = new ProcHostSignals.CpuTimes(Total: 2000, Idle: 1050);

            // 1000 ticks passed, 250 of them idle: 75 % busy.
            ProcHostSignals.BusyBetween(before, after).Should().Be(0.75);
        }

        [Fact]
        public void No_time_between_readings_reads_as_idle_rather_than_dividing_by_zero()
        {
            var same = new ProcHostSignals.CpuTimes(1000, 800);

            ProcHostSignals.BusyBetween(same, same).Should().Be(0);
        }

        [Fact]
        public void Reads_mem_available_in_bytes()
        {
            ProcHostSignals.ParseMemInfoKb("MemAvailable:   12345678 kB", "MemAvailable:").Should().Be(12345678L * 1024);
            ProcHostSignals.ParseMemInfoKb("MemFree:   1 kB", "MemAvailable:").Should().BeNull();
        }
    }
}
