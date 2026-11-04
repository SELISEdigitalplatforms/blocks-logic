using System.Text.Json;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Functions.DomainService.Events;
using Functions.DomainService.Services;
using Workflow.DomainService.Utils;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The one event a function may send (FunctionEvent), its shape, and who may send it. A function can never pick a
    /// queue or add a field the catalog does not know; a Public function sends nothing.
    /// </summary>
    public class FunctionEventCatalogTests
    {
        private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

        [Fact]
        public void The_catalog_has_exactly_FunctionEvent_with_its_fixed_queue()
        {
            FunctionEventCatalog.All.Select(d => d.Type).Should().Equal("FunctionEvent");

            // The type name is the routing key (Genesis routes by typeof(T).Name): it must be the real class.
            FunctionEventCatalog.FunctionEventType.Should().Be(nameof(FunctionEvent));

            FunctionEventCatalog.Find("FunctionEvent")!.Queue.Should().Be("blocks_logic_function_event_listener");
            FunctionEventCatalog.Find("FunctionEvent")!.PublicAllowed.Should().BeFalse();
            FunctionEventCatalog.Find("SendMail").Should().BeNull();
            FunctionEventCatalog.Find("Anything").Should().BeNull();
        }

        [Theory]
        [InlineData("order.created", true)]
        [InlineData("abc", true)]
        [InlineData("a-b_c.d9", true)]
        [InlineData("ab", false)]
        [InlineData("Order.created", false)]
        [InlineData("1order", false)]
        [InlineData("order created", false)]
        [InlineData("order.created\n", false)]
        [InlineData("", false)]
        public void A_name_follows_the_pattern(string name, bool valid)
        {
            FunctionEventCatalog.IsValidName(name).Should().Be(valid);
        }

        [Fact]
        public void A_name_of_64_characters_is_the_longest()
        {
            FunctionEventCatalog.IsValidName("a" + new string('b', 63)).Should().BeTrue();
            FunctionEventCatalog.IsValidName("a" + new string('b', 64)).Should().BeFalse();
        }

        [Fact]
        public void FunctionEvent_takes_only_name_and_data_and_a_valid_name()
        {
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":"order.created","data":{"id":1}}""").Should().BeNull();
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":"order.created"}""").Should().BeNull();
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":"order.created","queue":"x"}""").Should().Contain("not \"queue\"");
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":"BAD NAME"}""").Should().Contain("name must match");
            FunctionEventCatalog.Validate("FunctionEvent", """{"data":{}}""").Should().Contain("name must match");
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":5}""").Should().Contain("name must match");
        }

        [Fact]
        public void An_unknown_type_is_refused()
        {
            FunctionEventCatalog.Validate("DeleteEverything", "{}").Should().Be("unknown event type");
            FunctionEventCatalog.Validate(null, "{}").Should().Be("unknown event type");
        }

        [Fact]
        public void A_payload_over_64_KB_is_refused_even_though_the_runner_checked_it()
        {
            var big = new string('x', FunctionEventCatalog.MaxPayloadBytes);

            FunctionEventCatalog.Validate("FunctionEvent", $$"""{"name":"order.created","data":"{{big}}"}""")
                .Should().Contain("over");
        }

        [Fact]
        public void The_built_FunctionEvent_carries_the_source_and_keeps_the_data_as_json_text()
        {
            var run = new FunctionRunEntity { ItemId = "run-1", FunctionId = "fn-1", InvokedBy = InvokedByType.Http };

            var message = FunctionEventCatalog.BuildFunctionEvent(
                Json("""{"name":"order.created","data":{"id":7}}"""), "run-1-1-1", "fn-1", "run-1", FunctionEventDepth.ForRun(run));

            message.EventId.Should().Be("run-1-1-1");
            message.Name.Should().Be("order.created");
            message.DataJson.Should().Be("""{"id":7}""");
            message.SourceFunctionId.Should().Be("fn-1");
            message.SourceRunId.Should().Be("run-1");
            message.Depth.Should().Be(0);
            FunctionEventCatalog.BuildFunctionEvent(Json("""{"name":"abc"}"""), "i", "f", "r", 0).DataJson.Should().Be("null");
        }

        [Fact]
        public void Depth_is_zero_unless_an_event_started_the_run()
        {
            FunctionEventDepth.ForRun(new FunctionRunEntity { InvokedBy = InvokedByType.Http }).Should().Be(0);
            FunctionEventDepth.ForRun(new FunctionRunEntity { InvokedBy = InvokedByType.Test }).Should().Be(0);
            FunctionEventDepth.ForRun(new FunctionRunEntity { InvokedBy = InvokedByType.Event }).Should().Be(FunctionEventDepth.EventStartedRunDepth);
        }

        // ---- who may send what ---------------------------------------------------------

        [Fact]
        public void A_token_function_may_send_FunctionEvent_only()
        {
            FunctionEventPolicy.AllowedTypes(AuthMode.Token).Should().Equal("FunctionEvent");
        }

        [Fact]
        public void A_public_function_may_send_no_events()
        {
            FunctionEventPolicy.AllowedTypes(AuthMode.Public).Should().BeEmpty();
        }

        // ---- the envelope -------------------------------------------------------------

        private static string Envelope(IReadOnlyCollection<string>? allowed)
        {
            var run = new FunctionRunEntity { ItemId = "run_1", FunctionId = "fn_1", Attempt = 1, TenantId = "t" };
            var function = new FunctionEntity { ItemId = "fn_1", Trigger = new TriggerConfig { AuthMode = AuthMode.Token } };
            return FunctionEnvelopeBuilder.Build(run, null, function, null, null, allowed);
        }

        [Fact]
        public void The_envelope_lists_the_allowed_event_types()
        {
            using var doc = JsonDocument.Parse(Envelope(["FunctionEvent"]));

            doc.RootElement.GetProperty("events").GetProperty("allowed").EnumerateArray().Select(e => e.GetString())
                .Should().Equal("FunctionEvent");
        }

        [Fact]
        public void An_envelope_without_events_has_no_events_key()
        {
            using var none = JsonDocument.Parse(Envelope(null));
            using var empty = JsonDocument.Parse(Envelope([]));

            none.RootElement.TryGetProperty("events", out _).Should().BeFalse();
            empty.RootElement.TryGetProperty("events", out _).Should().BeFalse();
        }

        [Fact]
        public void The_envelope_for_a_public_function_has_no_events_key()
        {
            using var doc = JsonDocument.Parse(Envelope(FunctionEventPolicy.AllowedTypes(AuthMode.Public)));

            doc.RootElement.TryGetProperty("events", out _).Should().BeFalse();
        }
    }
}
