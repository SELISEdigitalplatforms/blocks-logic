using System.Text.Json;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Protocol;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Function events, runner side. The sandbox is untrusted — tenant code can write any line to
    /// fd 1 — so the parser's checks are tested as if the line were forged.
    /// </summary>
    public class FunctionEventsTests
    {
        private const string RunPrefix = "run1-1-";
        private static readonly string[] Both = [EventTypes.SendMail, EventTypes.FunctionEvent];

        private static string Event(int n, string type = "SendMail", string payload = """{"to":"a@b.c"}""") =>
            $$"""{"id":"{{RunPrefix}}{{n}}","type":"{{type}}","payload":{{payload}}}""";

        private static string ResultLine(string events, string value = "1") =>
            $$"""{"t":"result","ok":true,"value":{{value}},"events":{{events}}}""" + "\n";

        private static EventsCheck Check(string eventsJson, string[]? allowed = null) =>
            SandboxOutputParser.ValidateEvents(eventsJson, allowed ?? Both, RunPrefix);

        // ---- parser: reading the line ------------------------------------------------

        [Fact]
        public void The_success_line_keeps_its_raw_events_beside_the_value()
        {
            var events = $"[{Event(1)}]";

            var output = new SandboxOutputParser().Parse(ResultLine(events, """{"a":1}"""));

            output.Ok.Should().BeTrue();
            output.ResultJson.Should().Be("""{"a":1}""");
            output.EventsJson.Should().Be(events);
        }

        [Fact]
        public void A_line_without_events_has_none_and_a_failure_line_never_carries_any()
        {
            new SandboxOutputParser().Parse("""{"t":"result","ok":true,"value":1}""" + "\n").EventsJson.Should().BeNull();

            var failed = new SandboxOutputParser().Parse(
                $$"""{"t":"result","ok":false,"code":"USER_RUNTIME_ERROR","message":"x","events":[{{Event(1)}}]}""" + "\n");
            failed.Ok.Should().BeFalse();
            failed.EventsJson.Should().BeNull();
        }

        [Fact]
        public void A_result_over_64_KB_with_events_is_still_read_whole()
        {
            // Other lines are clipped at 64 KB; the result line may hold the value and the events.
            var value = JsonSerializer.Serialize(new string('x', 200 * 1024));

            var output = new SandboxOutputParser().Parse(ResultLine($"[{Event(1)}]", value));

            output.Ok.Should().BeTrue();
            output.ResultJson.Should().Be(value);
            output.EventsJson.Should().NotBeNull();
        }

        // ---- parser: validation ------------------------------------------------------

        [Fact]
        public void Valid_events_are_accepted_as_they_are()
        {
            var json = $"[{Event(1)},{Event(2, "FunctionEvent", """{"name":"order.created","data":{"id":7}}""")}]";

            var check = Check(json);

            check.Rejected.Should().BeNull();
            check.Count.Should().Be(2);
            using var accepted = JsonDocument.Parse(check.Json!);
            accepted.RootElement.GetArrayLength().Should().Be(2);
            check.Json.Should().Be(json);
        }

        [Fact]
        public void No_events_is_not_a_rejection()
        {
            SandboxOutputParser.ValidateEvents(null, Both).Should().Be(EventsCheck.None);
            Check("[]").Should().Be(EventsCheck.None);
        }

        public static TheoryData<string, string> Violations => new()
        {
            { "not a list", """{"id":"x"}""" },
            { "not json", "[{" },
            { "an item that is not an object", "[1]" },
            { "no id", """[{"type":"SendMail","payload":{}}]""" },
            { "a numeric id", """[{"id":5,"type":"SendMail","payload":{}}]""" },
            { "an id of another run", """[{"id":"run2-1-1","type":"SendMail","payload":{}}]""" },
            { "no type", $$$"""[{"id":"{{{RunPrefix}}}1","payload":{}}]""" },
            { "an unknown type", $$$"""[{"id":"{{{RunPrefix}}}1","type":"DeleteEverything","payload":{}}]""" },
            { "no payload", $$$"""[{"id":"{{{RunPrefix}}}1","type":"SendMail"}]""" },
            { "an array payload", $$$"""[{"id":"{{{RunPrefix}}}1","type":"SendMail","payload":[1]}]""" },
            { "a FunctionEvent without a name", $$$"""[{"id":"{{{RunPrefix}}}1","type":"FunctionEvent","payload":{"data":1}}]""" },
            { "a FunctionEvent with a bad name", $$$"""[{"id":"{{{RunPrefix}}}1","type":"FunctionEvent","payload":{"name":"Order.Created"}}]""" },
            { "a FunctionEvent name with a trailing newline", $$$"""[{"id":"{{{RunPrefix}}}1","type":"FunctionEvent","payload":{"name":"abc\n"}}]""" },
        };

        [Theory]
        [MemberData(nameof(Violations))]
        public void A_violation_drops_the_events_and_says_why(string _, string json)
        {
            var check = Check(json);

            check.Json.Should().BeNull();
            check.Count.Should().Be(0);
            check.Rejected.Should().StartWith("EVENTS_REJECTED: ");
        }

        [Fact]
        public void A_type_the_function_may_not_send_is_rejected_even_when_it_is_a_known_type()
        {
            Check($"[{Event(1, "SendMail")}]", [EventTypes.FunctionEvent]).Rejected.Should().Contain("not allowed");
            Check($"[{Event(1, "SendMail")}]", []).Rejected.Should().Contain("not allowed");
        }

        [Fact]
        public void An_id_may_not_repeat()
        {
            Check($"[{Event(1)},{Event(1)}]").Rejected.Should().Contain("repeats an id");
        }

        [Fact]
        public void More_than_fifty_events_are_rejected_and_fifty_are_not()
        {
            Check("[" + string.Join(",", Enumerable.Range(1, Ceilings.EventsPerCall).Select(i => Event(i))) + "]")
                .Rejected.Should().BeNull();
            Check("[" + string.Join(",", Enumerable.Range(1, Ceilings.EventsPerCall + 1).Select(i => Event(i))) + "]")
                .Rejected.Should().Contain("51 events");
        }

        [Fact]
        public void A_payload_over_64_KB_is_rejected()
        {
            var big = $$"""{"a":"{{new string('x', Ceilings.EventPayloadBytes)}}"}""";

            Check($"[{Event(1, payload: big)}]").Rejected.Should().Contain("payload over");
        }

        [Fact]
        public void Payloads_over_512_KB_together_are_rejected()
        {
            var chunk = $$"""{"a":"{{new string('x', 60 * 1024)}}"}""";
            var nine = string.Join(",", Enumerable.Range(1, 9).Select(i => Event(i, payload: chunk)));

            Check($"[{nine}]").Rejected.Should().Contain("together");
        }

        [Fact]
        public void The_reason_never_quotes_a_payload_or_a_type_name()
        {
            var secret = "sk_live_topsecret";

            var check = Check($$$"""[{"id":"{{{RunPrefix}}}1","type":"{{{secret}}}","payload":{"k":"{{{secret}}}"}}]""");

            check.Rejected.Should().NotContain(secret);
        }

        [Fact]
        public void A_forged_line_in_the_middle_of_other_output_cannot_slip_events_past_the_check()
        {
            // Tenant code writing its own result line gets the same treatment as the real one:
            // it is parsed, and its events still have to pass the same validation.
            var stdout =
                """{"t":"log","ts":"2026-01-01T00:00:00Z","level":"info","msg":"hi"}""" + "\n" +
                ResultLine("""[{"id":"run9-1-1","type":"SendMail","payload":{"to":"victim@x.y"}}]""");

            var output = new SandboxOutputParser().Parse(stdout);
            var check = SandboxOutputParser.ValidateEvents(output.EventsJson, Both, RunPrefix);

            output.Ok.Should().BeTrue("the result itself is kept");
            check.Json.Should().BeNull();
            check.Rejected.Should().StartWith("EVENTS_REJECTED");
        }

        // ---- envelope: trace and events ---------------------------------------------

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

        [Fact]
        public void The_events_object_passes_the_screen_and_survives_WithTrace_unchanged()
        {
            var queued = """{"run":{"id":"r"},"events":{"allowed":["SendMail","FunctionEvent"]},"input":{}}""";

            ExecutionEnvelope.Screen(queued);
            var envelope = ExecutionEnvelope.WithTrace(queued, Traceparent, []);

            using var doc = JsonDocument.Parse(envelope);
            doc.RootElement.GetProperty("events").GetRawText().Should().Be("""{"allowed":["SendMail","FunctionEvent"]}""");
            ExecutionEnvelope.AllowedEventTypes(envelope).Should().Equal("SendMail", "FunctionEvent");
        }

        [Theory]
        [InlineData("""{"run":{"id":"r"}}""")]
        [InlineData("""{"events":null}""")]
        [InlineData("""{"events":{"allowed":"SendMail"}}""")]
        [InlineData("""{"events":{"allowed":[1,null]}}""")]
        [InlineData("not json")]
        public void A_missing_or_malformed_allowed_list_means_no_events(string envelope)
        {
            ExecutionEnvelope.AllowedEventTypes(envelope).Should().BeEmpty();
        }
    }
}
