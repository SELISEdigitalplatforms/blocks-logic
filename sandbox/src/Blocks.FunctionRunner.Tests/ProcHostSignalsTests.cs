using Blocks.FunctionRunner.Admission;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// PSI is the signal the whole controller turns on, and it is read as text out of
    /// <c>/proc</c>. The samples below are literal copies of what the kernel writes, including
    /// the <c>full</c> line that <c>/proc/pressure/cpu</c> only grew in 5.13 — a parser that
    /// mistook a missing or malformed line for a large number would shrink a healthy host, and
    /// one that mistook it for garbage would grow a stalling one.
    /// </summary>
    public class ProcHostSignalsTests
    {
        private const string IdleCpu =
            "some avg10=0.00 avg60=0.00 avg300=0.00 total=0\n" +
            "full avg10=0.00 avg60=0.00 avg300=0.00 total=0\n";

        private const string BusyCpu =
            "some avg10=31.42 avg60=18.07 avg300=6.55 total=1420933514\n" +
            "full avg10=0.00 avg60=0.00 avg300=0.00 total=0\n";

        private const string BusyMemory =
            "some avg10=2.71 avg60=1.13 avg300=0.42 total=96551403\n" +
            "full avg10=1.09 avg60=0.55 avg300=0.19 total=42117905\n";

        /// <summary>A pre-5.13 kernel: /proc/pressure/cpu has a some line and nothing else.</summary>
        private const string CpuWithoutFullLine =
            "some avg10=4.20 avg60=2.10 avg300=0.70 total=99887766\n";

        [Fact]
        public void Reads_avg10_from_a_real_pressure_file()
        {
            ProcHostSignals.ParsePressure(BusyCpu, "some").Should().Be(31.42);
            ProcHostSignals.ParsePressure(BusyMemory, "some").Should().Be(2.71);
            ProcHostSignals.ParsePressure(BusyMemory, "full").Should().Be(1.09);
        }

        [Fact]
        public void Reads_an_idle_host_as_no_stall_at_all()
        {
            ProcHostSignals.ParsePressure(IdleCpu, "some").Should().Be(0);
            ProcHostSignals.ParsePressure(IdleCpu, "full").Should().Be(0);
        }

        [Fact]
        public void Reads_a_missing_line_as_calm_rather_than_as_pressure()
        {
            ProcHostSignals.ParsePressure(CpuWithoutFullLine, "full").Should().Be(0);
            ProcHostSignals.ParsePressure(string.Empty, "some").Should().Be(0);
            ProcHostSignals.ParsePressure("some avg60=1.00 total=5\n", "some").Should().Be(0);
            ProcHostSignals.ParsePressure("some avg10=notanumber avg60=1.00\n", "some").Should().Be(0);
        }

        [Fact]
        public void Does_not_mistake_the_full_line_for_the_some_line()
        {
            // They differ by a prefix and nothing else, and reading memory's `full` as its `some`
            // would under-report the one stall the controller reacts to hardest.
            ProcHostSignals.ParsePressure(BusyMemory, "some").Should().NotBe(
                ProcHostSignals.ParsePressure(BusyMemory, "full"));
        }

        [Fact]
        public void Handles_the_trailing_field_on_the_last_line_without_a_newline()
        {
            ProcHostSignals.ParsePressure("some avg10=7.50 avg60=1.00 avg300=0.10 total=12", "some")
                .Should().Be(7.50);
        }
    }
}
