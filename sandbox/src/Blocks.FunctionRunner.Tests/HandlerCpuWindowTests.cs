using System.Diagnostics;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A single run's CPU over the handler's own window (2026-10-08): the container's total held
    /// gVisor and Node starting at the 1 CPU boost, so a trivial Test run read "243 / 100 m".
    /// </summary>
    public class HandlerCpuWindowTests
    {
        private static (HandlerCpuWindow Window, Queue<long?> Readings) Create(params long?[] readings)
        {
            var queue = new Queue<long?>(readings);
            return (new HandlerCpuWindow(() => queue.Count > 0 ? queue.Dequeue() : null, Stopwatch.StartNew()), queue);
        }

        [Fact]
        public void Reads_the_host_counter_at_started_and_at_result_only()
        {
            var (window, readings) = Create(5_000_000, 5_012_400);

            window.OnText("{\"t\":\"log\",\"msg\":\"loading\"}\n");
            window.OnText("{\"t\":\"started\",\"at\":1}\n{\"t\":\"log\",\"msg\":\"hi\"}\n");
            window.OnText("{\"t\":\"result\",\"ok\":true,\"value\":1}\n");
            window.OnText("{\"t\":\"started\",\"at\":2}\n{\"t\":\"result\",\"ok\":true}\n");

            readings.Should().BeEmpty("one reading at each end, none after");
            window.Measured().Should().NotBeNull();
            window.Measured()!.Value.CpuMs.Should().Be(12);
            window.Measured()!.Value.WindowMs.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void Finds_markers_split_across_reads_and_both_in_one_read()
        {
            var (split, _) = Create(1_000, 3_000);
            split.OnText("{\"t\":\"sta");
            split.OnText("rted\",\"at\":1}\n{\"t\":\"res");
            split.OnText("ult\",\"ok\":true}\n");
            split.Measured()!.Value.CpuMs.Should().Be(2);

            var (together, _) = Create(1_000, 9_000);
            together.OnText("{\"t\":\"started\",\"at\":1}\n{\"t\":\"result\",\"ok\":true}\n");
            together.Measured()!.Value.CpuMs.Should().Be(8);
        }

        [Fact]
        public void A_marker_inside_a_log_message_is_escaped_and_does_not_count()
        {
            var (window, readings) = Create(1_000, 2_000);
            window.OnText("{\"t\":\"log\",\"msg\":\"{\\\"t\\\":\\\"started\\\"} {\\\"t\\\":\\\"result\\\"}\"}\n");

            readings.Should().HaveCount(2, "nothing was read");
            window.Measured().Should().BeNull();
        }

        [Fact]
        public void A_result_before_any_start_gives_no_window()
        {
            var (window, _) = Create(1_000, 2_000);
            window.OnText("{\"t\":\"result\",\"ok\":false,\"code\":\"RUNTIME_START_FAILED\"}\n");

            window.Measured().Should().BeNull();
        }

        [Fact]
        public void No_result_line_gives_no_window()
        {
            // Killed or crashed mid-handler: the end was never read, and the cgroup is gone by now.
            var (window, _) = Create(1_000, 2_000);
            window.OnText("{\"t\":\"started\",\"at\":1}\n");

            window.Measured().Should().BeNull();
        }

        [Fact]
        public void An_unreadable_cgroup_or_a_counter_that_went_back_gives_no_window()
        {
            var (unreadable, _) = Create(null, 2_000);
            unreadable.OnText("{\"t\":\"started\"}\n{\"t\":\"result\"}\n");
            unreadable.Measured().Should().BeNull();

            var (backwards, _) = Create(5_000, 4_000);
            backwards.OnText("{\"t\":\"started\"}\n{\"t\":\"result\"}\n");
            backwards.Measured().Should().BeNull();
        }

        [Fact]
        public void Any_cpu_measured_is_at_least_one_ms()
        {
            var (window, _) = Create(1_000, 1_000);
            window.OnText("{\"t\":\"started\"}\n{\"t\":\"result\"}\n");

            window.Measured()!.Value.CpuMs.Should().Be(1);
        }
    }
}
