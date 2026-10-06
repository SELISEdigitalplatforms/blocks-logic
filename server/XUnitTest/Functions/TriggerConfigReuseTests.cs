using System.Text;
using System.Text.Json;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;
using Functions.DomainService.Validation;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The trigger settings added for sandbox reuse, synchronous answers and the extra verbs
    /// (sandbox/REUSE.md). Every one of them must default to the behaviour before it existed —
    /// a stored document that predates them, and a client that never sends them, change nothing.
    /// </summary>
    public class TriggerConfigReuseTests
    {
        // ------------------------------------------------------------------ defaults ----

        [Fact]
        public void A_new_trigger_defaults_to_todays_behaviour()
        {
            var trigger = new TriggerConfig();

            trigger.ReuseSandbox.Should().BeFalse();
            trigger.ResponseMode.Should().BeNull("null is async");
            TriggerConfig.IsSync(trigger.ResponseMode).Should().BeFalse();
            trigger.HttpMethods.Should().BeNull("null is the legacy single method");
            FunctionHttpInputBuilder.AllowedVerbs(trigger).Should().Equal("POST");
            typeof(TriggerConfig).GetProperty("AllowSetCookie").Should().BeNull("the cookie feature was removed");
        }

        [Fact]
        public void A_stored_document_from_before_the_fields_reads_unchanged()
        {
            // Exactly what an older deploy wrote: none of the new elements.
            var stored = new BsonDocument
            {
                { "HttpEnabled", true },
                { "HttpMethod", 1 },
                { "AuthMode", 1 },
                { "Roles", new BsonArray() },
                { "Permissions", new BsonArray() },
                { "RoleMatch", 0 },
                { "PermissionMatch", 0 },
                { "WorkflowEnabled", true },
            };

            var trigger = BsonSerializer.Deserialize<TriggerConfig>(stored);

            trigger.HttpMethod.Should().Be(HttpTriggerMethod.Get);
            trigger.ReuseSandbox.Should().BeFalse();
            trigger.ResponseMode.Should().BeNull();
            trigger.HttpMethods.Should().BeNull();
            FunctionHttpInputBuilder.AllowedVerbs(trigger).Should().Equal("GET");
        }

        // ------------------------------------------------- rolling deploy / rollback ----

        private static readonly string[] NewTriggerElements = ["HttpMethods", "ReuseSandbox", "ResponseMode", "AllowSetCookie"];

        [Fact]
        public void A_default_trigger_writes_none_of_the_new_elements()
        {
            var document = new TriggerConfig().ToBsonDocument();

            document.Names.Should().NotIntersectWith(NewTriggerElements);
            document.Names.Should().BeEquivalentTo(
                ["HttpEnabled", "HttpMethod", "AuthMode", "Roles", "Permissions", "RoleMatch", "PermissionMatch", "Combine", "WorkflowEnabled"]);
        }

        [Fact]
        public void A_saved_trigger_with_untouched_settings_writes_none_of_the_new_elements()
        {
            // What the console sends for a function whose owner never touched the new switches.
            var saved = FunctionService.NormalizeForStorage(
                new TriggerConfig { HttpMethods = [], ResponseMode = "async", ReuseSandbox = false });

            saved.ToBsonDocument().Names.Should().NotIntersectWith(NewTriggerElements);
        }

        [Fact]
        public void Saving_keeps_real_choices_and_normalises_them()
        {
            var saved = FunctionService.NormalizeForStorage(
                new TriggerConfig { HttpMethods = ["get", "Put", "GET"], ResponseMode = "sync", ReuseSandbox = true });

            saved.HttpMethods.Should().Equal("GET", "PUT");
            saved.ResponseMode.Should().Be("sync");
            saved.ToBsonDocument().Names.Should().Contain(["HttpMethods", "ReuseSandbox", "ResponseMode"]);
        }

        [Fact]
        public void A_default_source_writes_none_of_the_new_elements()
            => new FunctionSource { IndexJs = "x", PackageJson = "{}" }.ToBsonDocument().Names
                .Should().NotContain("AllowInstallScripts");

        /// <summary>The trigger class as the previous release had it: no new members, no IgnoreExtraElements.</summary>
        private sealed class OldTriggerConfig
        {
            public bool HttpEnabled { get; set; } = true;
            public HttpTriggerMethod HttpMethod { get; set; } = HttpTriggerMethod.Post;
            public AuthMode AuthMode { get; set; } = AuthMode.Token;
            public List<string> Roles { get; set; } = [];
            public List<string> Permissions { get; set; } = [];
            public MatchMode RoleMatch { get; set; } = MatchMode.Any;
            public MatchMode PermissionMatch { get; set; } = MatchMode.Any;
            public AccessCombine Combine { get; set; } = AccessCombine.Or;
            public bool WorkflowEnabled { get; set; } = true;
        }

        /// <summary>The source class as the previous release had it.</summary>
        private sealed class OldFunctionSource
        {
            public string IndexJs { get; set; } = string.Empty;
            public string PackageJson { get; set; } = string.Empty;
            public string? LockJson { get; set; }
        }

        [Fact]
        public void An_old_pod_reads_what_the_new_code_writes_for_an_untouched_function()
        {
            var trigger = FunctionService.NormalizeForStorage(new TriggerConfig { HttpMethods = [], ResponseMode = "async" });
            var source = new FunctionSource { IndexJs = "x", PackageJson = "{}" };

            var readTrigger = () => BsonSerializer.Deserialize<OldTriggerConfig>(trigger.ToBsonDocument());
            var readSource = () => BsonSerializer.Deserialize<OldFunctionSource>(source.ToBsonDocument());

            readTrigger.Should().NotThrow();
            readSource.Should().NotThrow();
        }

        [Fact]
        public void The_old_shape_really_would_throw_on_a_new_element_which_is_why_defaults_are_not_written()
        {
            // Proves the test above tests something: the old class map fails on any new element.
            var withReuse = new TriggerConfig { ReuseSandbox = true }.ToBsonDocument();

            var read = () => BsonSerializer.Deserialize<OldTriggerConfig>(withReuse);

            read.Should().Throw<FormatException>();
        }

        [Fact]
        public void The_new_shape_ignores_elements_a_later_release_may_add()
        {
            var document = new TriggerConfig().ToBsonDocument();
            document.Add("SomethingFromTheFuture", 1);
            var source = new FunctionSource().ToBsonDocument();
            source.Add("SomethingFromTheFuture", 1);

            ((Action)(() => BsonSerializer.Deserialize<TriggerConfig>(document))).Should().NotThrow();
            ((Action)(() => BsonSerializer.Deserialize<FunctionSource>(source))).Should().NotThrow();
        }

        [Fact]
        public void A_version_document_round_trips_the_new_settings()
        {
            // The deployed snapshot is what the route reads, so the fields must survive Mongo.
            var version = new FunctionVersionEntity
            {
                ItemId = "v-1",
                Trigger = new TriggerConfig
                {
                    ReuseSandbox = true,
                    ResponseMode = "sync",
                    HttpMethods = ["GET", "DELETE"],
                },
            };

            var back = BsonSerializer.Deserialize<FunctionVersionEntity>(version.ToBsonDocument());

            back.Trigger.ReuseSandbox.Should().BeTrue();
            back.Trigger.ResponseMode.Should().Be("sync");
            back.Trigger.HttpMethods.Should().Equal("GET", "DELETE");
        }

        [Fact]
        public void A_run_document_carries_reuse_requested_only_when_set()
        {
            new FunctionRunEntity { ItemId = "r" }.ToBsonDocument().Names.Should().NotContain("ReuseRequested");
            new FunctionRunEntity { ItemId = "r", ReuseRequested = true }.ToBsonDocument()["ReuseRequested"].AsBoolean.Should().BeTrue();
        }

        // ---------------------------------------------------------------------- wire ----

        [Fact]
        public void A_save_request_without_the_fields_deserializes_to_the_defaults()
        {
            var request = JsonSerializer.Deserialize<SaveFunctionRequestDto>(
                """{"functionId":"fn","trigger":{"httpEnabled":true,"httpMethod":"Post"}}""",
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            request.Trigger.ReuseSandbox.Should().BeFalse();
            request.Trigger.ResponseMode.Should().BeNull();
            request.Trigger.HttpMethods.Should().BeNull();
        }

        [Fact]
        public void A_save_request_carries_the_fields_the_client_sends_and_ignores_a_stale_cookie_flag()
        {
            var request = JsonSerializer.Deserialize<SaveFunctionRequestDto>(
                """{"functionId":"fn","trigger":{"reuseSandbox":true,"responseMode":"sync","httpMethods":["GET","PUT"],"allowSetCookie":true}}""",
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            request.Trigger.ReuseSandbox.Should().BeTrue();
            request.Trigger.ResponseMode.Should().Be("sync");
            request.Trigger.HttpMethods.Should().Equal("GET", "PUT");
        }

        [Fact]
        public void The_wire_values_the_console_sends_round_trip_unchanged()
        {
            // The console's exact shape: upper-case verbs as strings, the legacy method as its enum
            // name, the response mode in lower case, a boolean.
            const string wire = """{"httpEnabled":true,"httpMethod":"Get","authMode":"Token","roles":[],"permissions":[],"roleMatch":"Any","permissionMatch":"Any","combine":"Or","workflowEnabled":true,"httpMethods":["GET","PUT","DELETE"],"reuseSandbox":true,"responseMode":"sync"}""";
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            var trigger = JsonSerializer.Deserialize<TriggerConfig>(wire, options)!;
            var back = JsonDocument.Parse(JsonSerializer.Serialize(trigger, options)).RootElement;

            back.GetProperty("httpMethod").GetString().Should().Be("Get");
            back.GetProperty("httpMethods").EnumerateArray().Select(e => e.GetString()).Should().Equal("GET", "PUT", "DELETE");
            back.GetProperty("responseMode").GetString().Should().Be("sync");
            back.GetProperty("reuseSandbox").GetBoolean().Should().BeTrue();
            new TriggerConfigValidator().Validate(trigger).IsValid.Should().BeTrue();
        }

        [Fact]
        public void The_run_detail_carries_the_warm_sandbox_fields_in_camel_case_and_null_when_unreported()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            var reported = JsonDocument.Parse(JsonSerializer.Serialize(global::Functions.DomainService.Dtos.Responses.RunDetailDto.From(
                new FunctionRunEntity { ItemId = "r", Reused = true, DiscardReason = "dirty:timer", HandoverMs = 3 }), options)).RootElement;
            reported.GetProperty("reused").GetBoolean().Should().BeTrue();
            reported.GetProperty("discardReason").GetString().Should().Be("dirty:timer");
            reported.GetProperty("handoverMs").GetInt64().Should().Be(3);

            var old = JsonDocument.Parse(JsonSerializer.Serialize(global::Functions.DomainService.Dtos.Responses.RunDetailDto.From(
                new FunctionRunEntity { ItemId = "r" }), options)).RootElement;
            old.GetProperty("reused").ValueKind.Should().Be(JsonValueKind.Null);
            old.GetProperty("discardReason").ValueKind.Should().Be(JsonValueKind.Null);
            old.GetProperty("handoverMs").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // ---------------------------------------------------------------- validation ----

        private static FluentValidation.Results.ValidationResult Validate(TriggerConfig trigger) =>
            new TriggerConfigValidator().Validate(trigger);

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        [InlineData("delete")]
        public void Each_routable_verb_is_accepted(string verb)
            => Validate(new TriggerConfig { HttpMethods = [verb] }).IsValid.Should().BeTrue();

        [Theory]
        [InlineData("HEAD")]
        [InlineData("OPTIONS")]
        [InlineData("TRACE")]
        [InlineData("CONNECT")]
        [InlineData("")]
        [InlineData("GETX")]
        public void A_verb_the_route_does_not_take_is_refused(string verb)
            => Validate(new TriggerConfig { HttpMethods = ["GET", verb] }).IsValid.Should().BeFalse();

        [Fact]
        public void A_verb_listed_twice_is_refused_whatever_its_case()
        {
            Validate(new TriggerConfig { HttpMethods = ["GET", "get"] }).IsValid.Should().BeFalse();
            Validate(new TriggerConfig { HttpMethods = ["PUT", "PUT"] }).IsValid.Should().BeFalse();
        }

        [Fact]
        public void An_empty_or_missing_verb_list_is_valid_and_means_the_legacy_method()
        {
            Validate(new TriggerConfig { HttpMethods = [] }).IsValid.Should().BeTrue();
            Validate(new TriggerConfig { HttpMethods = null }).IsValid.Should().BeTrue();
            FunctionHttpInputBuilder.AllowedVerbs(new TriggerConfig { HttpMethods = [] }).Should().Equal("POST");
        }

        [Theory]
        [InlineData("async", true)]
        [InlineData("sync", true)]
        [InlineData(null, true)]
        [InlineData("SYNC", false)]
        [InlineData("stream", false)]
        [InlineData("", false)]
        public void Only_the_two_response_modes_are_accepted(string? mode, bool valid)
            => Validate(new TriggerConfig { ResponseMode = mode }).IsValid.Should().Be(valid);

        // ------------------------------------------------------------ allowed verbs ----

        [Fact]
        public void The_listed_verbs_replace_the_legacy_method_upper_cased_and_unique()
        {
            var trigger = new TriggerConfig { HttpMethod = HttpTriggerMethod.Post, HttpMethods = ["get", "Delete", "GET"] };

            FunctionHttpInputBuilder.AllowedVerbs(trigger).Should().Equal("GET", "DELETE");
        }

        [Fact]
        public void A_stored_stray_verb_never_widens_the_route()
        {
            var trigger = new TriggerConfig { HttpMethods = ["TRACE", "PATCH"] };
            FunctionHttpInputBuilder.AllowedVerbs(trigger).Should().Equal("PATCH");

            // Nothing usable left → the legacy method, never "nothing allowed" and never TRACE.
            FunctionHttpInputBuilder.AllowedVerbs(new TriggerConfig { HttpMethods = ["TRACE"] }).Should().Equal("POST");
        }

        [Fact]
        public void A_test_uses_the_legacy_method_while_it_is_listed_otherwise_the_first_verb()
        {
            FunctionHttpInputBuilder.TestVerb(new TriggerConfig { HttpMethod = HttpTriggerMethod.Post, HttpMethods = ["GET", "POST"] })
                .Should().Be("POST");
            FunctionHttpInputBuilder.TestVerb(new TriggerConfig { HttpMethod = HttpTriggerMethod.Post, HttpMethods = ["PUT", "GET"] })
                .Should().Be("PUT");
            FunctionHttpInputBuilder.TestVerb(new TriggerConfig { HttpMethod = HttpTriggerMethod.Get })
                .Should().Be("GET");
        }

        // ------------------------------------------------------- input for every verb ----

        private static JsonElement Build(string method, string? body, string query = "") =>
            JsonDocument.Parse(FunctionHttpInputBuilder.Build(new InvokeFunctionRequestDto
            {
                Method = method,
                Path = "orders/42",
                Body = body is null ? null : Encoding.UTF8.GetBytes(body),
                ContentType = body is null ? null : "application/json",
                Query = query.Length == 0
                    ? new Dictionary<string, string[]>()
                    : new Dictionary<string, string[]> { ["q"] = [query] },
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            })).RootElement.Clone();

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public void Every_verb_hands_the_handler_method_path_query_and_body(string verb)
        {
            var root = Build(verb.ToLowerInvariant(), "{\"id\":7}", query: "x");

            root.GetProperty("method").GetString().Should().Be(verb);
            root.GetProperty("path").GetString().Should().Be("orders/42");
            root.GetProperty("query").GetProperty("q").GetString().Should().Be("x");
            root.GetProperty("body").GetProperty("id").GetInt32().Should().Be(7);
        }

        [Theory]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public void A_verb_sent_without_a_body_has_a_null_body(string verb)
            => Build(verb, null).GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);

        [Theory]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public void A_test_of_a_body_verb_carries_the_payload_as_the_body(string verb)
        {
            var root = JsonDocument.Parse(FunctionHttpInputBuilder.ForTest("{\"a\":1}", verb)).RootElement;

            root.GetProperty("method").GetString().Should().Be(verb);
            root.GetProperty("body").GetProperty("a").GetInt32().Should().Be(1);
            root.GetProperty("query").EnumerateObject().Should().BeEmpty();
        }

        [Fact]
        public void The_verb_overload_of_ForTest_matches_the_enum_one_for_the_legacy_methods()
        {
            FunctionHttpInputBuilder.ForTest("{\"a\":1}", "GET")
                .Should().Be(FunctionHttpInputBuilder.ForTest("{\"a\":1}", HttpTriggerMethod.Get));
            FunctionHttpInputBuilder.ForTest("{\"a\":1}", "POST")
                .Should().Be(FunctionHttpInputBuilder.ForTest("{\"a\":1}", HttpTriggerMethod.Post));
        }
    }
}
