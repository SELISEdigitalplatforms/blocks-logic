using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The envelope is the only channel into a sandbox, so these tests are less about shape
    /// than about what must never be in it. Spec §17 is the list; a failure here is a
    /// credential disclosure to tenant code, which is the worst thing this codebase can do.
    /// </summary>
    public class FunctionEnvelopeBuilderTests
    {
        private static BlocksContext Context(bool authenticated = true) => BlocksContext.Create(
            tenantId: "tenant_1",
            roles: ["Admin", "Editor"],
            userId: "user_1",
            isAuthenticated: authenticated,
            requestUri: "/api/fn/fn_reconcile",
            organizationId: "org_1",
            expireOn: DateTime.UtcNow.AddHours(1),
            email: "user@example.com",
            permissions: ["orders.read"],
            userName: "user",
            phoneNumber: null,
            displayName: "User One",
            oauthToken: "SUPER-SECRET-BEARER-TOKEN",
            originalTenantId: "tenant_1",
            applicationDomain: "app.example.com",
            impersonated: false,
            impersonationSessionId: null);

        private static FunctionEntity Function(AuthMode mode = AuthMode.Token) => new()
        {
            ItemId = "fn_1",
            Name = "Reconcile",
            Trigger = new TriggerConfig { AuthMode = mode },
            Limits = new FunctionLimits { TimeoutSeconds = 25 },
            Variables = [new VariableBinding { Key = "REGION", Value = "ch" }],
        };

        private static FunctionRunEntity Run() => new()
        {
            ItemId = "run_1",
            FunctionId = "fn_1",
            VersionNumber = 7,
            Attempt = 2,
            InvokedBy = InvokedByType.Http,
            TenantId = "tenant_1",
        };

        private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

        [Fact]
        public void Builds_the_documented_envelope_shape()
        {
            var json = FunctionEnvelopeBuilder.Build(Run(), null, Function(), Context(), "{\"orderId\":\"123\"}");

            using var doc = Parse(json);
            var root = doc.RootElement;

            root.GetProperty("run").GetProperty("id").GetString().Should().Be("run_1");
            root.GetProperty("run").GetProperty("version").GetInt32().Should().Be(7);
            root.GetProperty("run").GetProperty("attempt").GetInt32().Should().Be(2);
            root.GetProperty("run").GetProperty("invokedBy").GetProperty("type").GetString().Should().Be("http");
            root.GetProperty("context").GetProperty("tenantId").GetString().Should().Be("tenant_1");
            root.GetProperty("env").GetProperty("REGION").GetString().Should().Be("ch");
            root.GetProperty("input").GetProperty("orderId").GetString().Should().Be("123");
            root.GetProperty("limits").GetProperty("timeoutMs").GetInt32().Should().Be(25_000);
        }

        [Fact]
        public void The_callers_oauth_token_never_reaches_the_sandbox()
        {
            // BlocksContext carries the caller's bearer token. It must not be forwarded, and
            // the envelope is built from named fields precisely so it cannot be by accident.
            var json = FunctionEnvelopeBuilder.Build(Run(), null, Function(), Context(), null);

            json.Should().NotContain("SUPER-SECRET-BEARER-TOKEN");
            json.Should().NotContainEquivalentOf("oauthToken");
        }

        [Fact]
        public void Only_the_documented_identity_fields_are_exposed()
        {
            var json = FunctionEnvelopeBuilder.Build(Run(), null, Function(), Context(), null);

            using var doc = Parse(json);
            var names = doc.RootElement.GetProperty("context").EnumerateObject()
                .Select(p => p.Name).OrderBy(n => n).ToArray();

            // Spec §16's public surface exactly — no more, so a new BlocksContext property
            // upstream cannot start leaking into sandboxes.
            names.Should().BeEquivalentTo(
                ["applicationDomain", "email", "impersonated", "isAuthenticated",
                 "organizationId", "permissions", "roles", "tenantId", "userId"]);
        }

        [Fact]
        public void A_public_function_gets_an_anonymous_context_even_with_a_valid_token()
        {
            // Spec §18: a public function must not inherit a privileged identity just because
            // the request happened to be authenticated.
            var json = FunctionEnvelopeBuilder.Build(Run(), null, Function(AuthMode.Public), Context(), null);

            using var doc = Parse(json);
            var context = doc.RootElement.GetProperty("context");

            context.GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
            context.GetProperty("userId").ValueKind.Should().Be(JsonValueKind.Null);
            context.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);
            context.GetProperty("roles").GetArrayLength().Should().Be(0);
            context.GetProperty("permissions").GetArrayLength().Should().Be(0);
        }

        [Fact]
        public void A_token_function_passes_the_real_identity_through()
        {
            var json = FunctionEnvelopeBuilder.Build(Run(), null, Function(), Context(), null);

            using var doc = Parse(json);
            var context = doc.RootElement.GetProperty("context");

            context.GetProperty("isAuthenticated").GetBoolean().Should().BeTrue();
            context.GetProperty("userId").GetString().Should().Be("user_1");
            context.GetProperty("roles").EnumerateArray().Select(r => r.GetString())
                .Should().Contain("Admin");
        }

        [Fact]
        public void A_missing_context_yields_an_unauthenticated_envelope_not_a_crash()
        {
            var json = FunctionEnvelopeBuilder.Build(Run(), null, Function(), null, null);

            using var doc = Parse(json);
            doc.RootElement.GetProperty("context").GetProperty("isAuthenticated").GetBoolean()
                .Should().BeFalse();
        }

        [Fact]
        public void The_version_snapshot_wins_over_the_editable_configuration()
        {
            // Editing a function must not change how an already-deployed version behaves.
            var version = new FunctionVersionEntity
            {
                Limits = new FunctionLimits { TimeoutSeconds = 5 },
                Variables = [new VariableBinding { Key = "REGION", Value = "us" }],
                Trigger = new TriggerConfig { AuthMode = AuthMode.Token },
            };

            var json = FunctionEnvelopeBuilder.Build(Run(), version, Function(), Context(), null);

            using var doc = Parse(json);
            doc.RootElement.GetProperty("limits").GetProperty("timeoutMs").GetInt32().Should().Be(5_000);
            doc.RootElement.GetProperty("env").GetProperty("REGION").GetString().Should().Be("us");
        }

        [Fact]
        public void Limits_are_clamped_before_they_reach_the_envelope()
        {
            var greedy = Function();
            greedy.Limits = new FunctionLimits { TimeoutSeconds = 86_400 };

            var json = FunctionEnvelopeBuilder.Build(Run(), null, greedy, Context(), null);

            using var doc = Parse(json);
            doc.RootElement.GetProperty("limits").GetProperty("timeoutMs").GetInt32()
                .Should().Be(FunctionLimits.Ceiling.TimeoutSeconds * 1000);
        }

        [Theory]
        [InlineData("STRIPE_API_KEY")]
        [InlineData("DB_PASSWORD")]
        [InlineData("WEBHOOK_SECRET")]
        [InlineData("AUTHORIZATION")]
        public void A_credential_shaped_variable_name_is_allowed_because_env_is_exempt(string key)
        {
            // These are the honest names for a bound configuration variable. env keys are
            // tenant-authored and BuildEnv copies nothing else in, so screening them blocked
            // only legitimate names — never the platform credential the screen exists for.
            var function = Function();
            function.Variables = [new VariableBinding { Key = key, Value = "value" }];

            var json = FunctionEnvelopeBuilder.Build(Run(), null, function, Context(), null);

            using var doc = Parse(json);
            doc.RootElement.GetProperty("env").GetProperty(key).GetString().Should().Be("value");
        }

        [Fact]
        public void The_exemption_is_the_envelopes_own_env_and_nothing_else()
        {
            // An object called "env" that the caller sent as input is not the env the exemption
            // is about, and must still be screened.
            var act = () => FunctionEnvelopeBuilder.Build(
                Run(), null, Function(), Context(), "{\"env\":{\"accessToken\":\"x\"}}");

            act.Should().Throw<FunctionEnvelopeBuilder.ForbiddenContentException>()
                .WithMessage("*input.env.accessToken*");
        }

        // ---------- {{secret.<id>}} in a variable value ----------

        private static FunctionEntity WithVariables(params (string Key, string Value)[] variables)
        {
            var function = Function();
            function.Variables = variables
                .Select(v => new VariableBinding { Key = v.Key, Value = v.Value })
                .ToList();
            return function;
        }

        [Fact]
        public void A_bound_variable_reaches_ctx_env_as_its_resolved_value()
        {
            var function = WithVariables(("STRIPE_API_KEY", "{{secret.sec_1}}"));
            var secrets = new Dictionary<string, string> { ["sec_1"] = "sk_live_9" };

            var json = FunctionEnvelopeBuilder.Build(Run(), null, function, Context(), null, secrets);

            using var doc = Parse(json);
            doc.RootElement.GetProperty("env").GetProperty("STRIPE_API_KEY").GetString()
                .Should().Be("sk_live_9");
        }

        [Fact]
        public void A_reference_embedded_in_a_longer_value_is_substituted_in_place()
        {
            var function = WithVariables(("AUTH", "Bearer {{secret.sec_1}} v2"));
            var secrets = new Dictionary<string, string> { ["sec_1"] = "tok" };

            var json = FunctionEnvelopeBuilder.Build(Run(), null, function, Context(), null, secrets);

            using var doc = Parse(json);
            doc.RootElement.GetProperty("env").GetProperty("AUTH").GetString()
                .Should().Be("Bearer tok v2");
        }

        [Fact]
        public void An_unresolved_reference_fails_the_run_rather_than_shipping_the_placeholder()
        {
            // Delivering "{{secret.sec_gone}}" would have the function present that text to a
            // provider as if it were a key — a 401 far away from the real cause.
            var function = WithVariables(("STRIPE_API_KEY", "{{secret.sec_gone}}"));

            var act = () => FunctionEnvelopeBuilder.Build(
                Run(), null, function, Context(), null, new Dictionary<string, string>());

            act.Should().Throw<FunctionEnvelopeBuilder.UnresolvedSecretException>()
                .WithMessage("*sec_gone*");
        }

        [Fact]
        public void An_unresolved_reference_names_the_id_and_never_a_value()
        {
            var function = WithVariables(("A", "{{secret.sec_1}}"), ("B", "{{secret.sec_2}}"));
            var secrets = new Dictionary<string, string> { ["sec_1"] = "sk_live_9" };

            var act = () => FunctionEnvelopeBuilder.Build(Run(), null, function, Context(), null, secrets);

            act.Should().Throw<FunctionEnvelopeBuilder.UnresolvedSecretException>()
                .Which.Message.Should().Contain("sec_2").And.NotContain("sk_live_9");
        }

        [Fact]
        public void A_plain_value_that_merely_mentions_secret_is_left_alone()
        {
            var function = WithVariables(("NOTE", "the secret.sauce is {{not a ref}}"));

            var json = FunctionEnvelopeBuilder.Build(Run(), null, function, Context(), null);

            using var doc = Parse(json);
            doc.RootElement.GetProperty("env").GetProperty("NOTE").GetString()
                .Should().Be("the secret.sauce is {{not a ref}}");
        }

        [Fact]
        public void Collected_ids_are_deduplicated_across_variables()
        {
            var function = WithVariables(
                ("A", "{{secret.sec_1}}"),
                ("B", "x {{secret.sec_1}} y {{secret.sec_2}}"),
                ("C", "plain"));

            FunctionEnvelopeBuilder.CollectSecretIds(null, function)
                .Should().BeEquivalentTo(["sec_1", "sec_2"]);
        }

        [Fact]
        public void Collected_ids_come_from_the_deployed_version_when_there_is_one()
        {
            // The version snapshot is what the run executes, so its references are the ones to
            // resolve — the editor's unsaved configuration must not decide what gets read.
            var version = new FunctionVersionEntity
            {
                Variables = [new VariableBinding { Key = "A", Value = "{{secret.from_version}}" }],
                Limits = new FunctionLimits { TimeoutSeconds = 10 },
                Trigger = new TriggerConfig { AuthMode = AuthMode.Token },
            };

            FunctionEnvelopeBuilder.CollectSecretIds(version, WithVariables(("A", "{{secret.from_draft}}")))
                .Should().BeEquivalentTo(["from_version"]);
        }

        [Fact]
        public void A_function_with_no_references_asks_for_no_secrets()
        {
            FunctionEnvelopeBuilder.CollectSecretIds(null, Function()).Should().BeEmpty();
        }

        [Fact]
        public void A_credential_shaped_key_deep_inside_the_input_is_refused()
        {
            var act = () => FunctionEnvelopeBuilder.Build(
                Run(), null, Function(), Context(),
                "{\"a\":{\"b\":[{\"c\":{\"refreshToken\":\"x\"}}]}}");

            act.Should().Throw<FunctionEnvelopeBuilder.ForbiddenContentException>();
        }

        [Fact]
        public void A_forbidden_word_in_a_value_is_allowed_because_only_keys_are_screened()
        {
            // Screening values would reject legitimate input; the rule is about structure.
            var act = () => FunctionEnvelopeBuilder.Build(
                Run(), null, Function(), Context(), "{\"note\":\"my password is hunter2\"}");

            act.Should().NotThrow();
        }

        [Fact]
        public void An_oversized_envelope_is_refused_at_the_input_ceiling()
        {
            var big = "{\"blob\":\"" + new string('x', 2 * 1024 * 1024) + "\"}";

            var act = () => FunctionEnvelopeBuilder.Build(Run(), null, Function(), Context(), big);

            act.Should().Throw<FunctionEnvelopeBuilder.ForbiddenContentException>()
                .WithMessage("*input ceiling*");
        }

        [Fact]
        public void Malformed_input_becomes_an_inspectable_string_rather_than_a_broken_envelope()
        {
            var json = FunctionEnvelopeBuilder.Build(Run(), null, Function(), Context(), "{not json");

            using var doc = Parse(json);
            doc.RootElement.GetProperty("input").ValueKind.Should().Be(JsonValueKind.String);
        }

        [Fact]
        public void Absent_input_is_an_explicit_null()
        {
            var json = FunctionEnvelopeBuilder.Build(Run(), null, Function(), Context(), null);

            using var doc = Parse(json);
            doc.RootElement.GetProperty("input").ValueKind.Should().Be(JsonValueKind.Null);
        }

        [Theory]
        [InlineData(InvokedByType.Http, "http")]
        [InlineData(InvokedByType.Workflow, "workflow")]
        [InlineData(InvokedByType.Test, "test")]
        [InlineData(InvokedByType.Replay, "replay")]
        public void Invocation_source_is_carried_through(InvokedByType type, string expected)
        {
            var run = Run();
            run.InvokedBy = type;

            var json = FunctionEnvelopeBuilder.Build(run, null, Function(), Context(), null);

            using var doc = Parse(json);
            doc.RootElement.GetProperty("run").GetProperty("invokedBy").GetProperty("type")
                .GetString().Should().Be(expected);
        }
    }
}
