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
    /// `maskedEnv` — the list of env keys whose values came from a secret, which the sandbox turns
    /// into its redaction list. It is the only thing standing between a resolved credential and
    /// the run's logs, so what belongs in it, and what must never be in the envelope beside it,
    /// are both pinned here.
    /// </summary>
    public class FunctionEnvelopeMaskingTests
    {
        private static FunctionRunEntity Run() => new()
        {
            ItemId = "run_1", FunctionId = "fn_1", VersionNumber = 1, Attempt = 1,
            InvokedBy = InvokedByType.Http, TenantId = "tenant_1",
        };

        private static FunctionEntity Function(params VariableBinding[] variables) => new()
        {
            ItemId = "fn_1",
            Trigger = new TriggerConfig { AuthMode = AuthMode.Token },
            Limits = new FunctionLimits(),
            Variables = [.. variables],
        };

        private static JsonElement Build(FunctionEntity function)
            => JsonDocument.Parse(FunctionEnvelopeBuilder.Build(Run(), null, function, null, null))
                .RootElement.Clone();

        private static string[] Masked(JsonElement root) =>
            [.. root.GetProperty("maskedEnv").EnumerateArray().Select(e => e.GetString()!)];

        [Fact]
        public void A_secret_backed_variable_is_named_for_masking_and_stays_a_reference()
        {
            // The runner resolves the reference right before the sandbox starts; the envelope the
            // control plane writes to Redis holds the reference and nothing else.
            var function = Function(
                new VariableBinding { Key = "TOKEN", Value = "{{secret.abc123}}" },
                new VariableBinding { Key = "API_BASE", Value = "https://api.example.com" });

            var root = Build(function);

            Masked(root).Should().Equal("TOKEN");
            root.GetProperty("env").GetProperty("TOKEN").GetString().Should().Be("{{secret.abc123}}");
            root.GetProperty("env").GetProperty("API_BASE").GetString().Should().Be("https://api.example.com");
        }

        [Fact]
        public void A_plain_variable_is_never_masked()
        {
            // Masking every variable would hide the ordinary configuration people log on purpose.
            var function = Function(new VariableBinding { Key = "API_BASE", Value = "https://api.example.com" });

            Masked(Build(function)).Should().BeEmpty();
        }

        [Fact]
        public void A_reference_spliced_into_a_larger_value_still_marks_the_key_and_is_left_in_place()
        {
            var function = Function(new VariableBinding { Key = "AUTH", Value = "Bearer {{secret.abc123}}" });

            var root = Build(function);

            Masked(root).Should().Equal("AUTH");
            root.GetProperty("env").GetProperty("AUTH").GetString().Should().Be("Bearer {{secret.abc123}}");
        }

        [Fact]
        public void The_marker_carries_keys_only()
        {
            var function = Function(new VariableBinding { Key = "TOKEN", Value = "{{secret.abc123}}" });

            var json = FunctionEnvelopeBuilder.Build(Run(), null, function, null, null);

            using var doc = JsonDocument.Parse(json);
            var marker = doc.RootElement.GetProperty("maskedEnv").GetRawText();
            marker.Should().NotContain("abc123", "the runner reads the id from env; the marker only says which keys");
            doc.RootElement.TryGetProperty("maskedValues", out _).Should().BeFalse(
                "maskedValues is the runner's to write, once it holds the values");
        }

        [Fact]
        public void The_marker_and_the_references_survive_the_envelope_screen()
        {
            // "envSecrets" would have been the obvious name and the screen rejects any property
            // whose name contains "secret" — on both sides. And the reference text itself lives
            // in env values, which are never screened. This asserts both stayed legal.
            var function = Function(
                new VariableBinding { Key = "TOKEN", Value = "{{secret.abc123}}" },
                new VariableBinding { Key = "STRIPE_SECRET_KEY", Value = "Bearer {{secret.abc123}} {{secret.def456}}" });
            var json = FunctionEnvelopeBuilder.Build(Run(), null, function, null, null);

            var act = () => FunctionEnvelopeBuilder.Screen(json);

            act.Should().NotThrow();
        }

        [Fact]
        public void A_reference_to_a_missing_secret_is_not_refused_at_invoke()
        {
            // Nothing is resolved here, so nothing can be missing here: the runner fails the run
            // as SecretUnresolved, naming the variable, before any sandbox starts.
            var function = Function(new VariableBinding { Key = "TOKEN", Value = "{{secret.gone}}" });

            var act = () => FunctionEnvelopeBuilder.Build(Run(), null, function, null, null);

            act.Should().NotThrow();
        }
    }
}
