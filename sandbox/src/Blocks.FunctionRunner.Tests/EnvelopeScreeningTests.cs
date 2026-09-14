using System.Text.Json;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The envelope is the only channel into a sandbox. Spec §17 lists what must never travel
    /// down it; this is the enforcement, and it is deliberately paranoid because the failure
    /// mode is silent credential disclosure to tenant code.
    /// </summary>
    public class EnvelopeScreeningTests
    {
        private const string Valid = """
            {"run":{"id":"run_1"},"context":{"tenantId":"t1"},"env":{"REGION":"ch"},"input":{"a":1}}
            """;

        [Fact]
        public void A_normal_envelope_passes()
        {
            var act = () => ExecutionEnvelope.Screen(Valid);
            act.Should().NotThrow();
        }

        [Theory]
        [InlineData("accessToken")]
        [InlineData("access_token")]
        [InlineData("refreshToken")]
        [InlineData("clientSecret")]
        [InlineData("connectionString")]
        [InlineData("MONGO_PASSWORD")]
        [InlineData("apiKey")]
        [InlineData("privateKey")]
        [InlineData("vaultToken")]
        [InlineData("Authorization")]
        [InlineData("stripe_secret")]
        [InlineData("db_credential")]
        public void A_credential_shaped_key_is_refused(string key)
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["run"] = new Dictionary<string, string> { ["id"] = "run_1" },
                ["env"] = new Dictionary<string, string> { [key] = "value" },
            });

            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>()
                .WithMessage($"*{key}*");
        }

        [Fact]
        public void Screening_reaches_arbitrary_depth()
        {
            const string json = """
                {"run":{"id":"r"},"input":{"a":{"b":[{"c":{"clientSecret":"leaked"}}]}}}
                """;

            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
        }

        [Fact]
        public void Case_does_not_help_an_attacker()
        {
            const string json = """{"run":{"id":"r"},"env":{"AcCeSsToKeN":"x"}}""";

            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
        }

        [Fact]
        public void A_forbidden_word_in_a_value_is_allowed_only_keys_are_screened()
        {
            // Screening values would reject legitimate input; the rule is about structure.
            const string json = """{"run":{"id":"r"},"input":{"note":"my password is hunter2"}}""";

            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().NotThrow();
        }

        [Fact]
        public void Malformed_json_is_refused_rather_than_passed_through()
        {
            var act = () => ExecutionEnvelope.Screen("{not json");

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>()
                .WithMessage("*not valid JSON*");
        }

        [Fact]
        public void An_oversized_envelope_is_refused_at_the_input_ceiling()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"fn-envelope-{Guid.NewGuid():N}");
            try
            {
                var padding = new string('x', 2 * 1024 * 1024);
                var json = $$$"""{"run":{"id":"r"},"input":{"blob":"{{{padding}}}"}}""";

                var act = () => ExecutionEnvelope.Write(dir, json);

                act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>()
                    .WithMessage("*input ceiling*");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void The_written_envelope_is_readable_by_the_sandbox_uid()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"fn-envelope-{Guid.NewGuid():N}");
            try
            {
                var path = ExecutionEnvelope.Write(dir, Valid);

                File.Exists(path).Should().BeTrue();
                File.ReadAllText(path).Should().Contain("run_1");

                // uid 10001 is not the runner's uid, so it needs "other" read on the file and
                // "other" execute on the directory to reach it — and nothing more.
                var fileMode = File.GetUnixFileMode(path);
                fileMode.Should().HaveFlag(UnixFileMode.OtherRead);
                fileMode.Should().NotHaveFlag(UnixFileMode.OtherWrite);

                var dirMode = File.GetUnixFileMode(dir);
                dirMode.Should().HaveFlag(UnixFileMode.OtherExecute);
                dirMode.Should().NotHaveFlag(UnixFileMode.OtherRead);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
