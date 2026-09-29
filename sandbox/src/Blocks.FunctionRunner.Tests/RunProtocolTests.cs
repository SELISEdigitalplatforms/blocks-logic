using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Redis;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Which run entries this runner executes. Version 2 entries carry secret references the
    /// runner resolves; version 1 entries (an older control plane) already hold the plaintext and
    /// run unchanged. Anything else is dead-lettered — never executed on a guess.
    /// </summary>
    public class RunProtocolTests
    {
        private static ClaimedEntry Entry(string? protocol)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["runId"] = "run_1" };
            if (protocol is not null) fields["protocol"] = protocol;
            return new ClaimedEntry("1-0", fields);
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("2", 2)]
        [InlineData(null, 1)]
        [InlineData("", 1)]
        public void Supported_versions_are_accepted(string? sent, int expected)
        {
            RunConsumerService.ReadProtocol(Entry(sent)).Should().Be(expected);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("3")]
        [InlineData("-1")]
        [InlineData("two")]
        [InlineData("2.0")]
        public void Anything_else_is_refused(string sent)
        {
            RunConsumerService.ReadProtocol(Entry(sent)).Should().BeNull();
        }

        [Fact]
        public void The_run_protocol_moved_past_the_one_old_runners_accept()
        {
            // Old runners accept exactly ProtocolVersion (1); this gap is what stops them running
            // an envelope that still holds references.
            RedisKeys.RunProtocolVersion.Should().BeGreaterThan(RedisKeys.ProtocolVersion);
            new RunJob { RunId = "r", FunctionId = "f", Image = "i" }.Protocol.Should().Be(RedisKeys.RunProtocolVersion);
        }
    }
}
