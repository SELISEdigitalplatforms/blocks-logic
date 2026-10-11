using Blocks.FunctionRunner.Admission;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The footprint is what turns a reservation from "the limit this run was permitted" into
    /// "what a run on this host has actually cost", so the density of the whole fleet rests on
    /// it. These tests hold it to the two properties that make that safe: it says nothing until
    /// it has seen enough, and it never claims a run is cheaper than its own cgroup allows.
    /// </summary>
    public class SandboxFootprintTests
    {
        private const long Mb = 1024 * 1024;
        private const long Limit = 128 * Mb;

        [Fact]
        public void Reserves_the_full_limit_until_there_is_enough_evidence()
        {
            var footprint = new SandboxFootprint();

            for (var sample = 0; sample < SandboxFootprint.MinimumSamples - 1; sample++)
            {
                footprint.Record(10 * Mb);
                footprint.ReservationFor(Limit).Should().Be(Limit, "a cold runner must not widen admission on a handful of cheap runs");
            }

            footprint.Record(10 * Mb);
            footprint.ReservationFor(Limit).Should().Be(10 * Mb);
        }

        [Fact]
        public void Reports_the_p95_and_not_the_mean()
        {
            var footprint = new SandboxFootprint();

            // 90 cheap runs and 10 expensive ones. The mean is 28 MB, which would under-reserve
            // exactly when a batch of the expensive ones arrives together; the p95 covers them.
            for (var sample = 0; sample < 90; sample++) footprint.Record(20 * Mb);
            for (var sample = 0; sample < 10; sample++) footprint.Record(100 * Mb);

            footprint.ReservationFor(Limit).Should().Be(100 * Mb);
        }

        [Fact]
        public void Never_reserves_more_than_the_run_is_allowed()
        {
            var footprint = new SandboxFootprint();

            // A peak above the limit cannot happen — the cgroup is the boundary — so if one is
            // ever recorded the samples are wrong and the limit is the only defensible answer.
            for (var sample = 0; sample < SandboxFootprint.MinimumSamples; sample++) footprint.Record(512 * Mb);

            footprint.ReservationFor(Limit).Should().Be(Limit);
        }

        [Fact]
        public void Ignores_absent_and_nonsensical_readings()
        {
            var footprint = new SandboxFootprint();

            for (var sample = 0; sample < SandboxFootprint.MinimumSamples * 2; sample++)
            {
                footprint.Record(null);
                footprint.Record(0);
                footprint.Record(-1);
            }

            footprint.SampleCount.Should().Be(0);
            footprint.ReservationFor(Limit).Should().Be(Limit);
        }

        [Fact]
        public void Forgets_a_workload_it_is_no_longer_running()
        {
            var footprint = new SandboxFootprint();

            for (var sample = 0; sample < SandboxFootprint.WindowSize; sample++) footprint.Record(100 * Mb);
            footprint.ReservationFor(Limit).Should().Be(100 * Mb);

            // A full window of the new workload displaces the old one entirely, so the estimate
            // follows what the host is running now rather than averaging over yesterday.
            for (var sample = 0; sample < SandboxFootprint.WindowSize; sample++) footprint.Record(20 * Mb);

            footprint.SampleCount.Should().Be(SandboxFootprint.WindowSize);
            footprint.ReservationFor(Limit).Should().Be(20 * Mb);
        }
    }
}
