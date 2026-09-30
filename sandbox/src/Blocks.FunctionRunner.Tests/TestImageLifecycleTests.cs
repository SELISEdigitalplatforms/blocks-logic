using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Test runs build an image for one run on one host and delete it. These pin the parts of
    /// that which do not need a Docker Engine: the image is never a registry reference, and one
    /// left behind by a runner that died is reclaimed.
    /// </summary>
    public sealed class TestImageLifecycleTests
    {
        private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        private static readonly Dictionary<string, string> TestLabels = new()
        {
            [BuildProcessor.FunctionImageLabel] = "true",
            [BuildProcessor.TestImageLabel] = "true",
        };

        [Fact]
        public void A_test_image_is_named_locally_and_can_never_be_pulled_from_a_registry()
        {
            // No host part: pushing it or pulling it would fail rather than reach any registry.
            var reference = TestConsumerService.TestImageRef("0F2A-B");

            reference.Should().Be("blocks-test/0f2a-b:local");
            reference.Should().NotContain(".").And.NotContain("127.0.0.1").And.NotContain("@");
        }

        [Fact]
        public void A_test_image_a_crashed_runner_left_is_reclaimed_after_half_an_hour()
        {
            ImageGc.IsStaleTestImage(TestLabels, Now - ImageGc.TestImageMaxAge, Now).Should().BeTrue();
            ImageGc.TestImageMaxAge.Should().Be(TimeSpan.FromMinutes(30));
        }

        [Fact]
        public void A_test_image_whose_run_may_still_be_going_is_left_alone()
        {
            ImageGc.IsStaleTestImage(TestLabels, Now - TimeSpan.FromMinutes(5), Now).Should().BeFalse();
        }

        [Fact]
        public void A_deployed_image_is_never_treated_as_a_test_image()
        {
            var deployed = new Dictionary<string, string> { [BuildProcessor.FunctionImageLabel] = "true" };

            ImageGc.IsTestImage(deployed).Should().BeFalse();
            ImageGc.IsStaleTestImage(deployed, Now.AddDays(-30), Now).Should().BeFalse();
            ImageGc.IsTestImage(null).Should().BeFalse();
        }
    }
}
