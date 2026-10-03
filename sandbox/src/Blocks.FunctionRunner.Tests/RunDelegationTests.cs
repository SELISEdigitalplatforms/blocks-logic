using System.Text.Json;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The pure half of delegated access: what the runner reads from the envelope's caller, and
    /// how it adds <c>blocks.accessToken</c> and masks it.
    /// </summary>
    public sealed class RunDelegationTests
    {
        private const string Token = "eyJhbGciOi.delegated.sig";

        private static string Envelope(object? context = null, object? extra = null)
        {
            var doc = new Dictionary<string, object?>
            {
                ["run"] = new { id = "r", attempt = 1 },
                ["context"] = context ?? new { tenantId = "t1", userId = "u1", isAuthenticated = true },
                ["env"] = new { A = "x" },
            };
            if (extra is not null)
            {
                foreach (var p in extra.GetType().GetProperties()) doc[p.Name] = p.GetValue(extra);
            }
            return JsonSerializer.Serialize(doc);
        }

        [Fact]
        public void ReadCaller_reads_tenant_user_and_authentication()
        {
            RunDelegation.ReadCaller(Envelope()).Should().Be(new RunDelegation.Caller("t1", "u1", true));
        }

        [Fact]
        public void ReadCaller_treats_a_public_or_missing_identity_as_unauthenticated()
        {
            RunDelegation.ReadCaller(Envelope(new { tenantId = "t1", userId = (string?)null, isAuthenticated = false }))
                .Should().Be(new RunDelegation.Caller("t1", null, false));
            RunDelegation.ReadCaller("""{"run":{"id":"r"}}""")
                .Should().Be(new RunDelegation.Caller(null, null, false));
            RunDelegation.ReadCaller(Envelope(new { tenantId = "t1", userId = "  ", isAuthenticated = "true" }))
                .Should().Be(new RunDelegation.Caller("t1", null, false), "only a JSON true authenticates, and a blank user is none");
        }

        [Fact]
        public void Apply_adds_the_token_and_masks_it()
        {
            using var doc = JsonDocument.Parse(RunDelegation.Apply(Envelope(), Token));

            doc.RootElement.GetProperty("blocks").GetProperty("accessToken").GetString().Should().Be(Token);
            doc.RootElement.GetProperty("maskedValues").EnumerateArray().Select(e => e.GetString())
                .Should().Equal(Token);
            doc.RootElement.GetProperty("env").GetProperty("A").GetString().Should().Be("x", "nothing else changes");
        }

        [Fact]
        public void Apply_keeps_the_secret_values_already_masked()
        {
            var envelope = Envelope(extra: new { maskedValues = new[] { "sk_live_1", Token } });

            using var doc = JsonDocument.Parse(RunDelegation.Apply(envelope, Token));

            doc.RootElement.GetProperty("maskedValues").EnumerateArray().Select(e => e.GetString())
                .Should().Equal(["sk_live_1", Token], "each value once");
        }

        [Fact]
        public void Apply_replaces_whatever_blocks_section_the_envelope_carried()
        {
            var envelope = Envelope(extra: new { blocks = new { accessToken = "planted", other = 1 } });

            using var doc = JsonDocument.Parse(RunDelegation.Apply(envelope, Token));

            var blocks = doc.RootElement.GetProperty("blocks");
            blocks.GetProperty("accessToken").GetString().Should().Be(Token);
            blocks.TryGetProperty("other", out _).Should().BeFalse();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Apply_refuses_a_blank_token(string token)
        {
            var act = () => RunDelegation.Apply(Envelope(), token);
            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData("{nope")]
        [InlineData("[]")]
        public void A_malformed_envelope_is_refused_as_forbidden_content(string envelope)
        {
            ((Action)(() => RunDelegation.ReadCaller(envelope)))
                .Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
            ((Action)(() => RunDelegation.Apply(envelope, Token)))
                .Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
        }

        [Fact]
        public void The_runners_screen_still_refuses_a_token_the_queue_carried()
        {
            // Apply runs after the screen on purpose. The same key arriving from the queue is a
            // control-plane mistake and must fail the run.
            var queued = Envelope(extra: new { blocks = new { accessToken = Token } });

            ((Action)(() => ExecutionEnvelope.Screen(queued)))
                .Should().Throw<ExecutionEnvelope.ForbiddenContentException>().WithMessage("*blocks.accessToken*");
        }

        [Fact]
        public void The_runner_added_token_passes_the_screen_on_its_one_path()
        {
            ((Action)(() => ExecutionEnvelope.Screen(RunDelegation.Apply(Envelope(), Token), delegatedToken: true)))
                .Should().NotThrow();
        }

        [Theory]
        [InlineData("""{"run":{"id":"r"},"context":{"accessToken":"x"},"blocks":{"accessToken":"t"}}""", "context.accessToken")]
        [InlineData("""{"run":{"id":"r"},"blocks":{"accessToken":"t","refreshToken":"r"}}""", "blocks.refreshToken")]
        [InlineData("""{"run":{"id":"r"},"blocks":{"nested":{"accessToken":"t"}}}""", "blocks.nested.accessToken")]
        [InlineData("""{"run":{"id":"r"},"context":{"blocks":{"accessToken":"t"}}}""", "context.blocks.accessToken")]
        [InlineData("""{"run":{"id":"r"},"accessToken":"t"}""", "accessToken")]
        public void The_exemption_covers_nothing_but_the_top_level_blocks_accessToken(string envelope, string path)
        {
            ((Action)(() => ExecutionEnvelope.Screen(envelope, delegatedToken: true)))
                .Should().Throw<ExecutionEnvelope.ForbiddenContentException>().WithMessage($"*'{path}'*");
        }

        [Fact]
        public void Writing_an_envelope_with_the_token_needs_the_runner_to_say_it_added_it()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"fn-dlg-{Guid.NewGuid():N}");
            try
            {
                var envelope = RunDelegation.Apply(Envelope(), Token);

                ((Action)(() => ExecutionEnvelope.Write(dir, envelope, (_, _) => { })))
                    .Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
                File.ReadAllText(ExecutionEnvelope.Write(dir, envelope, (_, _) => { }, delegatedToken: true))
                    .Should().Contain(Token);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }
    }
}
