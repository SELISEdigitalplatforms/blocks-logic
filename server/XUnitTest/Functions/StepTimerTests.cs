using FluentAssertions;
using Functions.DomainService.Utils;
using Xunit;

namespace XUnitTest.Functions
{
    /// <summary>The step-time format a call's timings travel in: Api → run entry → runner → result entry → run.</summary>
    public class StepTimerTests
    {
        [Fact]
        public void Marks_round_trip_through_the_compact_form()
        {
            var timer = new StepTimer();
            timer.Mark("function");
            timer.Mark("version");

            var parsed = StepTimer.ParseCompact(timer.ToCompact());

            parsed.Select(t => t.Step).Should().Equal("function", "version");
            parsed.Should().OnlyContain(t => t.Group == t.Step && t.Ms >= 0);
        }

        [Fact]
        public void Grouped_names_split_on_the_first_dot()
        {
            var parsed = StepTimer.ParseCompact("api.function=35;queue=12;handover.token=69");

            parsed.Select(t => (t.Group, t.Step, t.Ms)).Should().Equal(
                ("api", "function", 35L), ("queue", "queue", 12L), ("handover", "token", 69L));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("a=-5;b=x;=3;c=")]
        public void Anything_malformed_is_skipped_not_thrown(string? compact) =>
            StepTimer.ParseCompact(compact).Should().BeEmpty();

        [Fact]
        public void An_oversized_field_is_capped()
        {
            var huge = string.Join(';', Enumerable.Range(0, 500).Select(i => $"s{i}=1"));
            StepTimer.ParseCompact(huge).Should().HaveCount(40);
        }
    }
}
