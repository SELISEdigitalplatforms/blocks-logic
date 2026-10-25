using System.Text.Json;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Functions.DomainService.Events;
using Functions.DomainService.Services;
using Mail.DomainService.Mails;
using Workflow.DomainService.Utils;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The two events a function may send, their shape, and what the platform forces. A function can never pick a
    /// queue, add a field the catalog does not know, or turn on the mail options the platform keeps off.
    /// </summary>
    public class FunctionEventCatalogTests
    {
        private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

        [Fact]
        public void The_catalog_has_exactly_SendMail_and_FunctionEvent_with_their_fixed_queues()
        {
            FunctionEventCatalog.All.Select(d => d.Type).Should().Equal("SendMail", "FunctionEvent");

            // The type name is the routing key (Genesis routes by typeof(T).Name): it must be the real class.
            FunctionEventCatalog.SendMailType.Should().Be(nameof(SendMail));
            FunctionEventCatalog.FunctionEventType.Should().Be(nameof(FunctionEvent));

            FunctionEventCatalog.Find("SendMail")!.Queue.Should().Be(LogicConstants.LogicMailQueueName).And.Be("blocks_email_listener");
            FunctionEventCatalog.Find("FunctionEvent")!.Queue.Should().Be("blocks_logic_function_event_listener");
            FunctionEventCatalog.Find("SendMail")!.PublicAllowed.Should().BeTrue();
            FunctionEventCatalog.Find("FunctionEvent")!.PublicAllowed.Should().BeFalse();
            FunctionEventCatalog.Find("Anything").Should().BeNull();
        }

        [Fact]
        public void Every_payload_template_is_a_valid_payload_for_its_own_event_in_camelCase()
        {
            foreach (var definition in FunctionEventCatalog.All)
            {
                FunctionEventCatalog.Validate(definition.Type, definition.PayloadTemplateJson, forSend: false)
                    .Should().BeNull($"the {definition.Type} template must pass its own check");
            }

            Json(FunctionEventCatalog.Find("SendMail")!.PayloadTemplateJson).EnumerateObject().Select(p => p.Name).Should().Equal(
                "purpose", "language", "to", "cc", "bcc", "subjectDataContext", "bodyDataContext", "attachments");
        }

        [Fact]
        public void SendMail_refuses_unknown_fields_including_the_ones_the_platform_owns()
        {
            foreach (var field in new[] { "sendPhoneNumberAsEmail", "correlationId", "replyTo", "context", "projectKey", "userId", "bogus" })
            {
                var payload = $$"""{"purpose":"x","to":["a@b.c"],"{{field}}":true}""";

                FunctionEventCatalog.Validate("SendMail", payload, forSend: true)
                    .Should().Contain("unknown field", $"{field} must not be accepted");
            }
        }

        [Fact]
        public void SendMail_checks_the_types_of_its_fields()
        {
            FunctionEventCatalog.Validate("SendMail", """{"to":"a@b.c"}""", forSend: false).Should().Contain("list of texts");
            FunctionEventCatalog.Validate("SendMail", """{"to":[1]}""", forSend: false).Should().Contain("texts");
            FunctionEventCatalog.Validate("SendMail", """{"purpose":3}""", forSend: false).Should().Contain("purpose");
            FunctionEventCatalog.Validate("SendMail", """{"bodyDataContext":{"a":{"nested":1}}}""", forSend: false).Should().Contain("bodyDataContext");
            FunctionEventCatalog.Validate("SendMail", """{"to":[],"TO":[]}""", forSend: false).Should().Contain("repeated");
            FunctionEventCatalog.Validate("SendMail", "[]", forSend: false).Should().Contain("JSON object");
            FunctionEventCatalog.Validate("SendMail", "not json", forSend: false).Should().Contain("not valid JSON");
        }

        [Fact]
        public void A_mail_about_to_be_sent_needs_a_purpose_and_a_recipient_but_a_template_does_not()
        {
            FunctionEventCatalog.Validate("SendMail", """{"purpose":"","to":[]}""", forSend: false).Should().BeNull();
            FunctionEventCatalog.Validate("SendMail", """{"purpose":"","to":["a@b.c"]}""", forSend: true).Should().Contain("purpose");
            FunctionEventCatalog.Validate("SendMail", """{"purpose":"welcome","to":[]}""", forSend: true).Should().Contain("recipient");
            FunctionEventCatalog.Validate("SendMail", """{"purpose":"welcome","to":["a@b.c"]}""", forSend: true).Should().BeNull();
        }

        [Fact]
        public void A_mail_has_at_most_100_recipients_in_all()
        {
            var many = string.Join(",", Enumerable.Range(0, 51).Select(i => $"\"u{i}@x.y\""));

            FunctionEventCatalog.Validate("SendMail", $$"""{"purpose":"p","to":[{{many}}],"cc":[{{many}}]}""", forSend: true)
                .Should().Contain("100 recipients");
        }

        [Fact]
        public void The_mail_that_is_built_has_the_phone_number_option_forced_off_and_the_event_id_as_correlation()
        {
            var payload = Json("""
                {"purpose":"order-confirm","language":"de","to":["a@b.c"],"cc":["c@d.e"],
                 "subjectDataContext":{"n":1},"bodyDataContext":{"ok":true,"name":"Ada"},"attachments":["f1"]}
                """);

            var mail = FunctionEventCatalog.BuildSendMail(payload, "run-1-1-1");

            mail.Purpose.Should().Be("order-confirm");
            mail.Language.Should().Be("de");
            mail.To.Should().Equal("a@b.c");
            mail.Cc.Should().Equal("c@d.e");
            mail.Bcc.Should().BeEmpty();
            mail.ReplyTo.Should().BeEmpty();
            mail.Attachments.Should().Equal("f1");
            mail.SubjectDataContext.Should().Equal(new Dictionary<string, string> { ["n"] = "1" });
            mail.BodyDataContext.Should().Equal(new Dictionary<string, string> { ["ok"] = "true", ["name"] = "Ada" });
            mail.SendPhoneNumberAsEmail.Should().BeFalse();
            mail.CorrelationId.Should().Be("run-1-1-1");
        }

        [Fact]
        public void A_payload_cannot_switch_the_phone_number_option_on_even_if_it_gets_past_the_check()
        {
            // Defence in depth: BuildSendMail ignores a field it does not know, whatever Validate said.
            var mail = FunctionEventCatalog.BuildSendMail(
                Json("""{"purpose":"p","to":["a@b.c"],"SendPhoneNumberAsEmail":true,"CorrelationId":"mine"}"""), "run-1-1-1");

            mail.SendPhoneNumberAsEmail.Should().BeFalse();
            mail.CorrelationId.Should().Be("run-1-1-1");
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
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":"order.created","data":{"id":1}}""", forSend: true).Should().BeNull();
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":"order.created"}""", forSend: true).Should().BeNull();
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":"order.created","queue":"x"}""", forSend: true).Should().Contain("not \"queue\"");
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":"BAD NAME"}""", forSend: true).Should().Contain("name must match");
            FunctionEventCatalog.Validate("FunctionEvent", """{"data":{}}""", forSend: true).Should().Contain("name must match");
            FunctionEventCatalog.Validate("FunctionEvent", """{"name":5}""", forSend: true).Should().Contain("name must match");
        }

        [Fact]
        public void An_unknown_type_is_refused()
        {
            FunctionEventCatalog.Validate("DeleteEverything", "{}", forSend: true).Should().Be("unknown event type");
            FunctionEventCatalog.Validate(null, "{}", forSend: false).Should().Be("unknown event type");
        }

        [Fact]
        public void A_payload_over_64_KB_is_refused_even_though_the_runner_checked_it()
        {
            var big = new string('x', FunctionEventCatalog.MaxPayloadBytes);

            FunctionEventCatalog.Validate("FunctionEvent", $$"""{"name":"order.created","data":"{{big}}"}""", forSend: true)
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

        private static FunctionEventSettingsEntity Settings(params string[] types) => new()
        {
            Events = [.. types.Select(t => new FunctionEventSetting { Type = t })],
        };

        [Fact]
        public void A_token_function_may_send_every_type_in_its_list()
        {
            FunctionEventPolicy.AllowedTypes(Settings("SendMail", "FunctionEvent"), AuthMode.Token)
                .Should().Equal("SendMail", "FunctionEvent");
        }

        [Fact]
        public void A_public_function_may_send_only_the_public_allowed_types()
        {
            FunctionEventPolicy.AllowedTypes(Settings("SendMail", "FunctionEvent"), AuthMode.Public).Should().Equal("SendMail");
        }

        [Fact]
        public void No_settings_and_unknown_types_mean_no_events()
        {
            FunctionEventPolicy.AllowedTypes(null, AuthMode.Token).Should().BeEmpty();
            FunctionEventPolicy.AllowedTypes(Settings("Nope", "SendMail", "SendMail"), AuthMode.Token).Should().Equal("SendMail");
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
            using var doc = JsonDocument.Parse(Envelope(["SendMail", "FunctionEvent"]));

            doc.RootElement.GetProperty("events").GetProperty("allowed").EnumerateArray().Select(e => e.GetString())
                .Should().Equal("SendMail", "FunctionEvent");
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
        public void The_envelope_for_a_public_function_carries_only_the_public_types_of_the_policy()
        {
            var allowed = FunctionEventPolicy.AllowedTypes(Settings("SendMail", "FunctionEvent"), AuthMode.Public);

            using var doc = JsonDocument.Parse(Envelope(allowed));

            doc.RootElement.GetProperty("events").GetProperty("allowed").EnumerateArray().Select(e => e.GetString())
                .Should().Equal("SendMail");
        }
    }
}
