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

        private static JsonElement Build(FunctionEntity function, IReadOnlyDictionary<string, string>? secrets)
            => JsonDocument.Parse(FunctionEnvelopeBuilder.Build(Run(), null, function, null, null, secrets))
                .RootElement.Clone();

        private static string[] Masked(JsonElement root) =>
            [.. root.GetProperty("maskedEnv").EnumerateArray().Select(e => e.GetString()!)];

        [Fact]
        public void A_secret_backed_variable_is_named_for_masking_and_its_value_is_the_resolved_one()
        {
            var function = Function(
                new VariableBinding { Key = "TOKEN", Value = "{{secret.abc123}}" },
                new VariableBinding { Key = "API_BASE", Value = "https://api.example.com" });

            var root = Build(function, new Dictionary<string, string> { ["abc123"] = "sk_live_9" });

            Masked(root).Should().Equal("TOKEN");
            root.GetProperty("env").GetProperty("TOKEN").GetString().Should().Be("sk_live_9");
            root.GetProperty("env").GetProperty("API_BASE").GetString().Should().Be("https://api.example.com");
        }

        [Fact]
        public void A_plain_variable_is_never_masked()
        {
            // Masking every variable would hide the ordinary configuration people log on purpose.
            var function = Function(new VariableBinding { Key = "API_BASE", Value = "https://api.example.com" });

            Masked(Build(function, null)).Should().BeEmpty();
        }

        [Fact]
        public void A_reference_spliced_into_a_larger_value_still_marks_the_key()
        {
            // The resolved value is a credential whether or not it is the whole string.
            var function = Function(new VariableBinding { Key = "AUTH", Value = "Bearer {{secret.abc123}}" });

            var root = Build(function, new Dictionary<string, string> { ["abc123"] = "sk_live_9" });

            Masked(root).Should().Equal("AUTH");
            root.GetProperty("env").GetProperty("AUTH").GetString().Should().Be("Bearer sk_live_9");
        }

        [Fact]
        public void The_marker_carries_keys_and_never_the_values()
        {
            var function = Function(new VariableBinding { Key = "TOKEN", Value = "{{secret.abc123}}" });

            var json = FunctionEnvelopeBuilder.Build(
                Run(), null, function, null, null, new Dictionary<string, string> { ["abc123"] = "sk_live_9" });

            using var doc = JsonDocument.Parse(json);
            var marker = doc.RootElement.GetProperty("maskedEnv").GetRawText();
            marker.Should().NotContain("sk_live_9", "the value is already in env; naming it twice is one more place to read it from");
            marker.Should().NotContain("abc123", "the secret's id is not needed to mask its value");
        }

        [Fact]
        public void The_marker_survives_the_envelope_screen()
        {
            // "envSecrets" would have been the obvious name and the screen rejects any property
            // whose name contains "secret" — on both sides. This asserts the name stayed legal.
            var function = Function(new VariableBinding { Key = "TOKEN", Value = "{{secret.abc123}}" });
            var json = FunctionEnvelopeBuilder.Build(
                Run(), null, function, null, null, new Dictionary<string, string> { ["abc123"] = "sk_live_9" });

            var act = () => FunctionEnvelopeBuilder.Screen(json);

            act.Should().NotThrow();
        }

        [Fact]
        public void An_unresolved_reference_fails_the_run_rather_than_shipping_the_placeholder()
        {
            var function = Function(new VariableBinding { Key = "TOKEN", Value = "{{secret.abc123}}" });

            var act = () => FunctionEnvelopeBuilder.Build(Run(), null, function, null, null, secrets: null);

            act.Should().Throw<FunctionEnvelopeBuilder.UnresolvedSecretException>()
                .WithMessage("*abc123*").And.Message.Should().NotContain("sk_live");
        }
    }
}
