using FluentAssertions;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Services;
using Xunit;

namespace XUnitTest.Functions
{
    /// <summary>
    /// GetRun shows the runner's live progress (kept only in Redis) for a run the record still has
    /// open, so the console does not show Queued for a whole build and run and then jump to done.
    /// </summary>
    public class FunctionRunLiveStatusTests
    {
        [Theory]
        [InlineData(RunStatus.Queued, "STARTING", null, RunStatus.Starting)]
        [InlineData(RunStatus.Queued, "RUNNING", null, RunStatus.Running)]
        [InlineData(RunStatus.Queued, "QUEUED", "building", RunStatus.Claimed)]
        [InlineData(RunStatus.Queued, null, "building", RunStatus.Claimed)]
        [InlineData(RunStatus.Queued, "RUNNING", "building", RunStatus.Running)]
        public void Shows_the_runners_progress(RunStatus recorded, string? live, string? phase, RunStatus expected)
            => FunctionRunService.LiveStatus(recorded, live, phase).Should().Be(expected);

        [Fact]
        public void Never_shows_an_outcome_before_it_is_recorded()
            => FunctionRunService.LiveStatus(RunStatus.Queued, "SUCCEEDED").Should().Be(RunStatus.Running);

        [Fact]
        public void Never_goes_backwards()
            => FunctionRunService.LiveStatus(RunStatus.Running, "STARTING").Should().Be(RunStatus.Running);

        [Fact]
        public void A_recorded_outcome_wins()
            => FunctionRunService.LiveStatus(RunStatus.Failed, "RUNNING").Should().Be(RunStatus.Failed);

        [Theory]
        [InlineData(null)]
        [InlineData("garbage")]
        public void Unknown_or_missing_live_status_keeps_the_record(string? live)
            => FunctionRunService.LiveStatus(RunStatus.Queued, live).Should().Be(RunStatus.Queued);

        [Fact]
        public void The_phase_field_matches_the_runner()
            => (FunctionQueueKeys.RunPhaseField, FunctionQueueKeys.RunPhaseBuilding)
                .Should().Be((Blocks.FunctionRunner.Contracts.RedisKeys.RunPhaseField,
                              Blocks.FunctionRunner.Contracts.RedisKeys.RunPhaseBuilding));
    }
}
