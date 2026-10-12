using System.Text.Json;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// What the runner resolves and what it hands the sandbox. The control plane leaves
    /// secret-bound variables as <c>{{secret.&lt;id&gt;}}</c> references; this is where they
    /// become values, and where the list the bootstrap masks with is completed.
    /// </summary>
    public sealed class EnvSecretReferencesTests
    {
        private static string Envelope(object env, object? maskedEnv = null, object? input = null, object? extra = null)
        {
            var doc = new Dictionary<string, object?>
            {
                ["run"] = new { id = "r1" },
                ["context"] = new { tenantId = "t1", userId = "u1", organizationId = "o1", roles = new[] { "admin" } },
                ["env"] = env,
                ["maskedEnv"] = maskedEnv ?? Array.Empty<string>(),
                ["input"] = input ?? new { },
                ["limits"] = new { timeoutMs = 5000 },
            };
            if (extra is not null)
            {
                foreach (var p in extra.GetType().GetProperties()) doc[p.Name] = p.GetValue(extra);
            }
            return JsonSerializer.Serialize(doc);
        }

        private static readonly string[] AuthOnly = ["AUTH"];
        private static readonly string[] Planted = ["planted"];

        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

        // ---------- Collect ----------

        [Fact]
        public void Whole_embedded_and_multiple_references_are_all_found_per_key()
        {
            var plan = EnvSecretReferences.Collect(Envelope(new
            {
                STRIPE_KEY = "{{secret.sec_1}}",
                AUTH = "Bearer {{secret.sec_2}}",
                PAIR = "{{secret.sec_1}}:{{secret.sec_3}}",
                PLAIN = "https://api.example.com",
            }));

            plan.IdsByKey.Keys.Should().BeEquivalentTo(["STRIPE_KEY", "AUTH", "PAIR"]);
            plan.IdsByKey["PAIR"].Should().Equal("sec_1", "sec_3");
            plan.Ids.Should().BeEquivalentTo(["sec_1", "sec_2", "sec_3"], "one lookup, each id once");
        }

        [Fact]
        public void The_callers_identity_is_read_from_the_envelope_context()
        {
            var plan = EnvSecretReferences.Collect(Envelope(new { A = "{{secret.s}}" }));

            plan.Caller.TenantId.Should().Be("t1");
            plan.Caller.UserId.Should().Be("u1");
            plan.Caller.OrganizationId.Should().Be("o1");
            plan.Caller.Roles.Should().Equal("admin");
        }

        [Fact]
        public void A_reference_in_the_callers_input_is_never_resolved()
        {
            // Otherwise a caller could post "{{secret.x}}" in a body and read the value back.
            var plan = EnvSecretReferences.Collect(Envelope(
                new { PLAIN = "x" },
                input: new { body = "{{secret.sec_1}}", nested = new { v = "{{secret.sec_2}}" } }));

            plan.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void Values_that_are_not_strings_or_only_look_like_references_are_ignored()
        {
            var plan = EnvSecretReferences.Collect(Envelope(new
            {
                NUM = 42,
                FLAG = true,
                NOTE = "the secret.sauce is {{not a ref}}",
                SPACED = "{{ secret.sec_1 }}",
            }));

            plan.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void An_envelope_without_env_asks_for_nothing()
        {
            EnvSecretReferences.Collect("""{"run":{"id":"r"}}""").IsEmpty.Should().BeTrue();
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        public void A_malformed_envelope_is_refused_as_forbidden_content(string raw)
        {
            var act = () => EnvSecretReferences.Collect(raw);

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
        }

        // ---------- Apply ----------

        private static readonly Dictionary<string, string> Values = new()
        {
            ["sec_1"] = "sk_live_one_111",
            ["sec_2"] = "tok_two_222",
            ["sec_3"] = "pw_three_333",
        };

        [Fact]
        public void References_become_values_in_place_and_everything_else_is_untouched()
        {
            var json = Envelope(new
            {
                STRIPE_KEY = "{{secret.sec_1}}",
                AUTH = "Bearer {{secret.sec_2}} v2",
                PAIR = "{{secret.sec_1}}:{{secret.sec_3}}",
                SAME_TWICE = "{{secret.sec_2}}{{secret.sec_2}}",
                PLAIN = "https://api.example.com",
            });
            var plan = EnvSecretReferences.Collect(json);

            var env = Parse(EnvSecretReferences.Apply(json, plan, Values)).GetProperty("env");

            env.GetProperty("STRIPE_KEY").GetString().Should().Be("sk_live_one_111");
            env.GetProperty("AUTH").GetString().Should().Be("Bearer tok_two_222 v2");
            env.GetProperty("PAIR").GetString().Should().Be("sk_live_one_111:pw_three_333");
            env.GetProperty("SAME_TWICE").GetString().Should().Be("tok_two_222tok_two_222");
            env.GetProperty("PLAIN").GetString().Should().Be("https://api.example.com");
        }

        [Fact]
        public void The_mask_list_covers_every_resolved_key_and_each_bare_value()
        {
            // maskedValues is what masks "tok_two_222" when a function logs only the token out of
            // "Bearer tok_two_222" — masking the whole env value would not.
            var json = Envelope(
                new { AUTH = "Bearer {{secret.sec_2}}", TOKEN = "{{secret.sec_2}}", OTHER = "{{secret.sec_1}}", PLAIN = "p" },
                maskedEnv: AuthOnly);
            var plan = EnvSecretReferences.Collect(json);

            var root = Parse(EnvSecretReferences.Apply(json, plan, Values));

            Strings(root.GetProperty("maskedEnv")).Should().BeEquivalentTo(["AUTH", "TOKEN", "OTHER"]);
            Strings(root.GetProperty("maskedValues")).Should().BeEquivalentTo(["tok_two_222", "sk_live_one_111"]);
        }

        [Fact]
        public void A_key_the_control_plane_forgot_to_mark_is_still_masked()
        {
            var json = Envelope(new { TOKEN = "{{secret.sec_1}}" }, maskedEnv: Array.Empty<string>());
            var plan = EnvSecretReferences.Collect(json);

            Strings(Parse(EnvSecretReferences.Apply(json, plan, Values)).GetProperty("maskedEnv"))
                .Should().Equal("TOKEN");
        }

        [Fact]
        public void Whatever_the_control_plane_put_in_maskedValues_is_replaced()
        {
            var json = Envelope(new { TOKEN = "{{secret.sec_1}}" }, extra: new { maskedValues = Planted });
            var plan = EnvSecretReferences.Collect(json);

            Strings(Parse(EnvSecretReferences.Apply(json, plan, Values)).GetProperty("maskedValues"))
                .Should().Equal("sk_live_one_111");
        }

        [Fact]
        public void Substitution_is_a_single_pass()
        {
            // A value that itself looks like a reference is delivered as text, not expanded again.
            var json = Envelope(new { A = "{{secret.outer}}" });
            var plan = EnvSecretReferences.Collect(json);

            var env = Parse(EnvSecretReferences.Apply(json, plan, new Dictionary<string, string>
            {
                ["outer"] = "{{secret.inner}}",
                ["inner"] = "must_not_appear",
            })).GetProperty("env");

            env.GetProperty("A").GetString().Should().Be("{{secret.inner}}");
        }

        [Fact]
        public void Values_with_quotes_backslashes_and_unicode_arrive_intact()
        {
            var tricky = "p\"a\\ss\nwörd</script>";
            var json = Envelope(new { A = "x {{secret.s}} y" });
            var plan = EnvSecretReferences.Collect(json);

            var env = Parse(EnvSecretReferences.Apply(json, plan, new Dictionary<string, string> { ["s"] = tricky }))
                .GetProperty("env");

            env.GetProperty("A").GetString().Should().Be($"x {tricky} y");
        }

        [Fact]
        public void Input_carrying_reference_text_is_passed_through_unresolved()
        {
            var json = Envelope(new { A = "{{secret.sec_1}}" }, input: new { body = "{{secret.sec_2}}" });
            var plan = EnvSecretReferences.Collect(json);

            var root = Parse(EnvSecretReferences.Apply(json, plan, Values));

            root.GetProperty("input").GetProperty("body").GetString().Should().Be("{{secret.sec_2}}");
            root.GetRawText().Should().NotContain("tok_two_222");
        }

        [Fact]
        public void The_resolved_envelope_still_passes_the_runners_screen()
        {
            var json = Envelope(new { STRIPE_SECRET_KEY = "{{secret.sec_1}}" });
            var plan = EnvSecretReferences.Collect(json);

            var act = () => ExecutionEnvelope.Screen(EnvSecretReferences.Apply(json, plan, Values));

            act.Should().NotThrow();
        }

        [Fact]
        public void An_unresolved_id_is_refused_and_named_without_any_value()
        {
            var json = Envelope(new { A = "{{secret.sec_1}}", B = "{{secret.gone}}" });
            var plan = EnvSecretReferences.Collect(json);

            var act = () => EnvSecretReferences.Apply(json, plan, Values);

            act.Should().Throw<KeyNotFoundException>()
                .Which.Message.Should().Contain("gone").And.NotContain("sk_live_one_111");
        }

        [Fact]
        public void An_envelope_with_nothing_to_resolve_is_returned_unchanged()
        {
            var json = Envelope(new { PLAIN = "p" });

            EnvSecretReferences.Apply(json, EnvSecretReferences.Collect(json), Values).Should().Be(json);
        }
    }
}
