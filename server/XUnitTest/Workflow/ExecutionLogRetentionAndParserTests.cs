using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.DomainService.Logging;

namespace XUnitTest.Workflow
{
    public class ExecutionLogLineParserTests
    {
        [Fact]
        public void ExecutionLine()
        {
            ExecutionLogLineParser.TryParse("[wf:execution.created] Execution created. Mode Test, trigger none.", out var line).Should().BeTrue();
            line.Should().Be(new ParsedExecutionLogLine("execution.created", null, null, "Execution created. Mode Test, trigger none."));
        }

        [Fact]
        public void NodeLineWithRunIndex()
        {
            ExecutionLogLineParser.TryParse("[wf:node.started] [node:3f9c#12] Node 'A' (webhook v1) started.", out var line).Should().BeTrue();
            line.Should().Be(new ParsedExecutionLogLine("node.started", "3f9c", 12, "Node 'A' (webhook v1) started."));
        }

        [Fact]
        public void NodeLineWithoutRunIndex()
        {
            ExecutionLogLineParser.TryParse("[wf:node.waiting] [node:c02d] Waiting for upstream nodes: 1 of 2 complete.", out var line).Should().BeTrue();
            line!.NodeId.Should().Be("c02d");
            line.RunIndex.Should().BeNull();
        }

        [Fact]
        public void BracketInsideTheText_StaysInTheText()
        {
            ExecutionLogLineParser.TryParse("[wf:node.started] [node:n1#1] Node 'a ] [node:x#9] b' started.", out var line).Should().BeTrue();
            line!.NodeId.Should().Be("n1");
            line.RunIndex.Should().Be(1);
            line.Text.Should().Be("Node 'a ] [node:x#9] b' started.");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Node n1 Using executor X.")]
        [InlineData("[wf:] text")]
        [InlineData("[wf:node] no dot")]
        [InlineData("[wf:Node.started] upper-case first segment")]
        [InlineData(" [wf:node.started] leading space")]
        public void NonStageLines_AreRejected(string? message)
        {
            ExecutionLogLineParser.TryParse(message, out var line).Should().BeFalse();
            line.Should().BeNull();
        }
    }

    public class LmtExecutionLogRetentionProviderTests
    {
        private sealed class FixedTime : TimeProvider
        {
            public DateTimeOffset Value { get; set; } = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Value;
        }

        private sealed class Provider : LmtExecutionLogRetentionProvider
        {
            private readonly Func<int?> _read;
            public int Reads { get; private set; }

            public Provider(Func<int?> read, TimeProvider time, int fallback = 30)
                : base(Options.Create(new ExecutionLogOptions { RetentionDays = fallback }), NullLogger<LmtExecutionLogRetentionProvider>.Instance, time)
            {
                _read = read;
            }

            protected override Task<int?> ReadHotRetentionDaysAsync(CancellationToken ct)
            {
                Reads++;
                return Task.FromResult(_read());
            }
        }

        [Fact]
        public async Task ReadsTheLmtValue()
        {
            var provider = new Provider(() => 14, new FixedTime());
            (await provider.GetRetentionDaysAsync()).Should().Be(14);
        }

        [Theory]
        [InlineData(null)]   // document or field missing
        [InlineData(0)]
        [InlineData(-3)]
        public async Task FallsBackToConfig_WhenMissingOrNotPositive(int? value)
        {
            var provider = new Provider(() => value, new FixedTime(), fallback: 21);
            (await provider.GetRetentionDaysAsync()).Should().Be(21);
        }

        [Fact]
        public async Task FallsBackToConfig_WhenTheReadThrows()
        {
            var provider = new Provider(() => throw new TimeoutException(), new FixedTime(), fallback: 21);
            (await provider.GetRetentionDaysAsync()).Should().Be(21);
        }

        [Fact]
        public async Task CachesForTenMinutes()
        {
            var time = new FixedTime();
            var provider = new Provider(() => 14, time);

            await provider.GetRetentionDaysAsync();
            time.Value += TimeSpan.FromMinutes(9);
            await provider.GetRetentionDaysAsync();
            provider.Reads.Should().Be(1);

            time.Value += TimeSpan.FromMinutes(2);
            await provider.GetRetentionDaysAsync();
            provider.Reads.Should().Be(2);
        }
    }
}
