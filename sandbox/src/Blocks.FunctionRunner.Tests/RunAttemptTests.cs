using Blocks.FunctionRunner.Redis;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The attempt a result reports is the control plane's, carried on the run entry. It used to
    /// be the stream's delivery count, which is a different number: a retry is a fresh entry whose
    /// delivery count starts at 1 again (so attempt 3 reported itself as attempt 1), and a
    /// redelivery of the same attempt counted up (so attempt 1 could report itself as 2). The
    /// attempt keys output-action idempotency, so both mistakes matter.
    /// </summary>
    public class RunAttemptTests
    {
        private static ClaimedEntry Entry(string? attempt)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runId"] = "run_1",
                ["image"] = "img@sha256:abc",
            };
            if (attempt is not null) fields["attempt"] = attempt;
            return new ClaimedEntry("1-0", fields);
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("2", 2)]
        [InlineData("7", 7)]
        public void The_attempt_the_control_plane_sent_is_the_one_reported(string sent, int expected)
        {
            RunConsumerService.ReadAttempt(Entry(sent)).Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("0")]
        [InlineData("-3")]
        [InlineData("two")]
        [InlineData("2.5")]
        [InlineData("+2")]
        [InlineData(" 2")]
        [InlineData("1e3")]
        [InlineData("99999999999999999999")]
        public void A_missing_or_invalid_attempt_reads_as_the_first(string? sent)
        {
            RunConsumerService.ReadAttempt(Entry(sent)).Should().Be(1);
        }

        [Fact]
        public void A_null_entry_is_refused()
        {
            var act = () => RunConsumerService.ReadAttempt(null!);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
