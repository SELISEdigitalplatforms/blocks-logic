using System.Diagnostics;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>The step times the runner adds to a result entry for the run's Timing group.</summary>
    public class HandoverTimingsTests
    {
        [Fact]
        public void The_apis_steps_are_carried_on_then_the_queue_then_the_handover()
        {
            var timings = new HandoverTimings(Stopwatch.StartNew());
            timings.ImageReady();
            timings.Admitted();
            timings.SandboxReady();
            timings.Secrets(170);
            timings.Token(70);

            var composed = HandoverTimings.Compose("function=35;version=34", 12, timings, 300);

            composed.Should().StartWith("api.function=35;api.version=34;queue=12;handover.image=");
            composed.Should().Contain("handover.secrets=170").And.Contain("handover.token=70").And.EndWith("handover.total=300");
        }

        [Fact]
        public void A_step_never_reached_is_left_out()
        {
            var timings = new HandoverTimings(Stopwatch.StartNew());
            timings.ImageReady();

            HandoverTimings.Compose(null, null, timings, null)
                .Should().NotContain("admission").And.NotContain("sandbox").And.NotContain("secrets");
        }

        [Fact]
        public void A_malformed_api_field_is_not_carried_on()
        {
            HandoverTimings.Compose("ok=1;bad;x=y;=3", null, null, null).Should().Be("api.ok=1");
        }
    }
}
