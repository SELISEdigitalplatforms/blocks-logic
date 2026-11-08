using System.Text.Json;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>The trace the runner sets on the envelope: never taken from the queue.</summary>
    public class EnvelopeTraceTests
    {
        private const string Traceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        [Fact]
        public void The_runner_sets_trace_and_replaces_one_the_control_plane_sent()
        {
            var queued = """{"run":{"id":"r"},"trace":{"traceparent":"00-forged","hosts":["evil.example"]},"input":{}}""";

            var envelope = ExecutionEnvelope.WithTrace(queued, Traceparent, ["api.blocks.test", " "]);

            using var doc = JsonDocument.Parse(envelope);
            var trace = doc.RootElement.GetProperty("trace");
            trace.GetProperty("traceparent").GetString().Should().Be(Traceparent);
            trace.GetProperty("hosts").EnumerateArray().Select(h => h.GetString()).Should().Equal("api.blocks.test");
            doc.RootElement.GetProperty("run").GetProperty("id").GetString().Should().Be("r");
            ExecutionEnvelope.Screen(envelope);
        }

        [Fact]
        public void Without_a_span_or_hosts_the_trace_is_empty_not_missing()
        {
            using var doc = JsonDocument.Parse(ExecutionEnvelope.WithTrace("""{"run":{"id":"r"}}""", null, []));

            var trace = doc.RootElement.GetProperty("trace");
            trace.GetProperty("traceparent").ValueKind.Should().Be(JsonValueKind.Null);
            trace.GetProperty("hosts").GetArrayLength().Should().Be(0);
        }

        [Fact]
        public void An_envelope_that_is_not_an_object_is_refused()
        {
            var act = () => ExecutionEnvelope.WithTrace("[1]", Traceparent, []);

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
        }
    }
}
