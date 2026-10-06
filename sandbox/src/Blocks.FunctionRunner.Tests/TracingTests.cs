using System.Diagnostics;
using Blocks.FunctionRunner.Tracing;
using FluentAssertions;
using OpenTelemetry;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Traces: a tag Genesis's exporter cannot store must never cost the whole batch, and a function
    /// call is one trace across the Api, the runner and the Worker.
    /// </summary>
    public class TracingTests
    {
        [Fact]
        public void A_delegate_tag_like_Genesis_RedisValue_Length_becomes_its_value()
        {
            using var activity = new Activity("Redis::MessageReceived").Start();
            long Length() => 42;
            activity.SetTag("MessageLength", new Func<long>(Length));
            activity.SetTag("Channel", "config");
            activity.SetTag("Odd", new object());

            new TraceTagSanitizer().OnEnd(activity);

            activity.GetTagItem("MessageLength").Should().Be(42L);
            activity.GetTagItem("Channel").Should().Be("config");
            activity.GetTagItem("Odd").Should().Be("Object", "the type, never the object's text");
        }

        [Fact]
        public void A_getter_that_throws_is_stored_as_unreadable_not_thrown()
        {
            using var activity = new Activity("x").Start();
            activity.SetTag("Bad", new Func<long>(() => throw new InvalidOperationException()));

            new TraceTagSanitizer().OnEnd(activity);

            activity.GetTagItem("Bad").Should().Be("unreadable");
        }

        [Fact]
        public void A_run_span_continues_the_callers_trace_and_carries_the_tenant()
        {
            using var source = new ActivitySource("test-run-tracing-" + Guid.NewGuid().ToString("N"));
            using var listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == source.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(listener);
            const string parent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

            using var span = RunTracing.Start(source, "Function::Run", ActivityKind.Consumer, parent, "tenant-42");

            span.Should().NotBeNull();
            span!.TraceId.ToString().Should().Be("0af7651916cd43dd8448eb211c80319c");
            span.ParentSpanId.ToString().Should().Be("b7ad6b7169203331");
            Baggage.GetBaggage("TenantId").Should().Be("tenant-42");
            RunTracing.CurrentTraceParent().Should().StartWith("00-0af7651916cd43dd8448eb211c80319c-");
        }

        [Fact]
        public void A_missing_or_broken_parent_starts_a_new_trace_and_no_source_is_a_no_op()
        {
            RunTracing.Start(null, "x", ActivityKind.Consumer, null, null).Should().BeNull();

            using var source = new ActivitySource("test-run-tracing-" + Guid.NewGuid().ToString("N"));
            using var listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == source.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(listener);
            using var span = RunTracing.Start(source, "x", ActivityKind.Consumer, "not-a-traceparent", null);
            span.Should().NotBeNull();
            span!.ParentSpanId.ToString().Should().Be("0000000000000000");
        }
    }
}
