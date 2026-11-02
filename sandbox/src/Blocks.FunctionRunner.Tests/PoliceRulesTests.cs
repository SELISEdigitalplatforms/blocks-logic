using Blocks.FunctionRunner.Health;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>What <c>fnctl police</c> calls a failure. Its exit code is what a monitor acts on.</summary>
    public sealed class PoliceRulesTests
    {
        private static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Exit_code_is_one_only_when_something_fails_not_for_warnings()
        {
            var ok = new PoliceResult("a", PoliceLevel.Ok, "");
            var warn = new PoliceResult("b", PoliceLevel.Warn, "");
            var fail = new PoliceResult("c", PoliceLevel.Fail, "");

            PoliceRules.ExitCode([ok, warn]).Should().Be(0);
            PoliceRules.ExitCode([ok, warn, fail]).Should().Be(1);
        }

        [Fact]
        public void A_paused_warm_sandbox_is_a_failure_only_past_max_age_plus_ten_minutes()
        {
            var justInside = new SandboxInfo("a", "paused", true, Noon.AddSeconds(-(3600 + 600)), null);
            var past = new SandboxInfo("b", "paused", true, Noon.AddSeconds(-(3600 + 601)), null);
            var notWarm = new SandboxInfo("c", "paused", false, Noon.AddDays(-1), null);

            PoliceRules.PausedTooLong([justInside, notWarm], Noon, 3600).Level.Should().Be(PoliceLevel.Ok);
            PoliceRules.PausedTooLong([justInside, past], Noon, 3600).Level.Should().Be(PoliceLevel.Fail);
        }

        [Fact]
        public void A_run_container_is_a_failure_past_the_hard_limit_plus_five_minutes_but_a_warm_one_is_not_judged_by_age()
        {
            var limit = Contracts.Ceilings.HardLimitSeconds + 300;
            var stuck = new SandboxInfo("a", "running", false, Noon.AddHours(-1), Noon.AddSeconds(-(limit + 1)));
            var fine = new SandboxInfo("b", "running", false, Noon.AddHours(-1), Noon.AddSeconds(-limit));
            var warmBusy = new SandboxInfo("c", "running", true, Noon.AddHours(-1), null);

            PoliceRules.RunningTooLong([fine, warmBusy], Noon).Level.Should().Be(PoliceLevel.Ok);
            PoliceRules.RunningTooLong([stuck, fine], Noon).Level.Should().Be(PoliceLevel.Fail);
        }

        [Fact]
        public void A_missing_or_old_gc_record_fails_unless_the_runner_only_just_started()
        {
            var fresh = new GcLast(Noon.AddHours(-1), 2);
            var stale = new GcLast(Noon.AddHours(-3), 2);

            PoliceRules.GcFresh(fresh, Noon, null).Level.Should().Be(PoliceLevel.Ok);
            PoliceRules.GcFresh(stale, Noon, TimeSpan.FromDays(2)).Level.Should().Be(PoliceLevel.Fail);
            PoliceRules.GcFresh(null, Noon, TimeSpan.FromDays(2)).Level.Should().Be(PoliceLevel.Fail);
            PoliceRules.GcFresh(null, Noon, TimeSpan.FromMinutes(5)).Level.Should().Be(PoliceLevel.Ok);
        }

        [Fact]
        public void A_sample_older_than_sixty_seconds_means_the_runner_is_not_recording()
        {
            MetricsSample At(DateTimeOffset ts) => new(ts, "r", true, new WarmSample(0, 0, 0),
                new Dictionary<string, StreamSample>(), new HostStats(null, null, null, null, null, null, null, null, null),
                new DockerStats(null, null, null, null));

            PoliceRules.SampleFresh(At(Noon.AddSeconds(-60)), Noon).Level.Should().Be(PoliceLevel.Ok);
            PoliceRules.SampleFresh(At(Noon.AddSeconds(-61)), Noon).Level.Should().Be(PoliceLevel.Fail);
            PoliceRules.SampleFresh(null, Noon).Level.Should().Be(PoliceLevel.Fail);
        }
    }
}
