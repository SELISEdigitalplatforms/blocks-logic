using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Blocks.FunctionRunner.Contracts;
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
                ["context"] = new Dictionary<string, string> { [key] = "value" },
            });

            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>()
                .WithMessage($"*{key}*");
        }

        [Theory]
        [InlineData("STRIPE_API_KEY")]
        [InlineData("MONGO_PASSWORD")]
        [InlineData("WEBHOOK_SECRET")]
        public void A_credential_shaped_key_under_env_is_allowed(string key)
        {
            // env keys are variable names the tenant wrote, and a variable may deliberately
            // carry a secret they bound to it — the honest name must not fail the run. Mirrors
            // FunctionEnvelopeBuilder.Screen; the two screens are meant to agree.
            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["run"] = new Dictionary<string, string> { ["id"] = "run_1" },
                ["env"] = new Dictionary<string, string> { [key] = "value" },
            });

            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().NotThrow();
        }

        [Theory]
        [InlineData("""{"run":{"id":"r"},"input":{"password":"hunter2"}}""")]
        [InlineData("""{"run":{"id":"r"},"input":{"user":{"credentials":{"password":"p","apiKey":"k"}}}}""")]
        [InlineData("""{"run":{"id":"r"},"input":{"body":{"client_secret":"s"},"query":{"access_token":"t"}}}""")]
        [InlineData("""{"run":{"id":"r"},"input":[{"refreshToken":"x"},{"a":[{"Authorization":"Bearer y"}]}]}""")]
        [InlineData("""{"run":{"id":"r"},"input":{"env":{"accessToken":"x"}}}""")]
        public void Credential_shaped_keys_anywhere_in_the_callers_input_are_allowed(string json)
        {
            // input is the caller's own payload — for an HTTP trigger its body and query. A
            // sign-up form posting a password is ordinary input; the caller already holds what
            // it sent. Mirrors FunctionEnvelopeBuilder.Screen's exemption on the control plane.
            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().NotThrow();
        }

        [Theory]
        [InlineData("""{"run":{"id":"r","accessToken":"x"},"input":{"password":"ok"}}""", "run.accessToken")]
        [InlineData("""{"context":{"caller":{"password":"x"}},"input":{"password":"ok"}}""", "context.caller.password")]
        [InlineData("""{"limits":{"vault_token":"x"},"input":{"password":"ok"}}""", "limits.vault_token")]
        [InlineData("""{"run":{"id":"r"},"input":{"body":{"password":"ok"},"headers":{"authorization":"Bearer x"}}}""", "input.headers.authorization")]
        [InlineData("""{"run":{"id":"r"},"serviceKey":"x"}""", "serviceKey")]
        public void The_input_exemption_does_not_extend_to_what_the_platform_writes(string json, string where)
        {
            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>()
                .WithMessage($"*'{where}'*");
        }

        [Theory]
        [InlineData("""{"context":{"input":{"password":"x"}}}""", "context.input.password")]
        [InlineData("""{"context":{"env":{"apiKey":"x"}}}""", "context.env.apiKey")]
        [InlineData("""{"run":{"input":[{"secret":"x"}]}}""", "run.input.[0].secret")]
        public void Only_the_top_level_input_and_env_are_exempt_not_objects_that_share_the_name(string json, string where)
        {
            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>()
                .WithMessage($"*'{where}'*");
        }

        [Fact]
        public void Screening_reaches_arbitrary_depth()
        {
            const string json = """
                {"run":{"id":"r"},"context":{"a":{"b":[{"c":{"clientSecret":"leaked"}}]}}}
                """;

            var act = () => ExecutionEnvelope.Screen(json);

            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
        }

        [Fact]
        public void Case_does_not_help_an_attacker()
        {
            const string json = """{"run":{"id":"r"},"context":{"AcCeSsToKeN":"x"}}""";

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

        private static string NewDir() => Path.Combine(Path.GetTempPath(), $"fn-envelope-{Guid.NewGuid():N}");

        private const UnixFileMode OwnerOnlyDirectory =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        private const UnixFileMode OwnerAndGroupRead = UnixFileMode.UserRead | UnixFileMode.GroupRead;

        /// <summary>The file's numeric group, which .NET has no managed API for.</summary>
        private static int GroupOf(string path)
        {
            using var stat = Process.Start(new ProcessStartInfo("stat", ["-c", "%g", path])
            {
                RedirectStandardOutput = true,
            })!;
            var output = stat.StandardOutput.ReadToEnd();
            stat.WaitForExit();
            return int.Parse(output.Trim(), CultureInfo.InvariantCulture);
        }

        [Fact]
        public void The_written_envelope_is_readable_by_the_sandbox_group_and_nobody_else()
        {
            // The envelope carries resolved secret variables. It used to be 0644 in a 0751
            // directory — readable by any account on the host that could name a run id.
            var dir = NewDir();
            var handedTo = new List<(string Path, int Gid)>();
            try
            {
                var path = ExecutionEnvelope.Write(dir, Valid, (p, gid) => handedTo.Add((p, gid)));

                File.ReadAllText(path).Should().Be(Valid);
                handedTo.Should().ContainSingle().Which.Should().Be((path, Ceilings.SandboxUid));

                File.GetUnixFileMode(path).Should().Be(OwnerAndGroupRead,
                    "0440: the runner and the sandbox's group, no one else, and nobody may write it");
                File.GetUnixFileMode(dir).Should().Be(OwnerOnlyDirectory,
                    "0700: the Engine resolves the bind source as root, so the sandbox needs no traversal");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [SkippableFact]
        public void The_real_handoff_gives_the_file_to_the_sandbox_gid()
        {
            // Only root can give a file to a group it is not in, so this proves the syscall, not
            // the provisioning; provision/40-runner-user.sh checks the runner's membership itself.
            Skip.IfNot(Environment.UserName == "root", "needs root, or membership of gid 10001");

            var dir = NewDir();
            try
            {
                var path = ExecutionEnvelope.Write(dir, Valid);

                GroupOf(path).Should().Be(Ceilings.SandboxUid);
                File.GetUnixFileMode(path).Should().Be(OwnerAndGroupRead);
                ExecutionEnvelope.ProbeHandoff(dir).Should().BeNull();
                Directory.EnumerateFileSystemEntries(dir).Should().ContainSingle("the probe cleans up after itself");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void A_failed_handoff_deletes_the_envelope_rather_than_leaving_it_behind()
        {
            var dir = NewDir();
            try
            {
                var act = () => ExecutionEnvelope.Write(dir, Valid,
                    (_, _) => throw new ExecutionEnvelope.HandoffException("EPERM"));

                act.Should().Throw<ExecutionEnvelope.HandoffException>();
                File.Exists(Path.Combine(dir, ExecutionEnvelope.FileName)).Should().BeFalse(
                    "an envelope the sandbox cannot read must not linger with the tenant's secrets in it");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void The_file_is_owner_only_until_the_group_is_the_sandboxes()
        {
            var dir = NewDir();
            UnixFileMode? modeAtHandoff = null;
            try
            {
                ExecutionEnvelope.Write(dir, Valid, (p, _) => modeAtHandoff = File.GetUnixFileMode(p));

                modeAtHandoff.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void A_world_readable_leftover_from_an_earlier_attempt_is_replaced_not_reused()
        {
            // A crashed attempt (or an older runner) leaves a 0644 file in a 0751 directory;
            // writing over it in place would keep that mode.
            var dir = NewDir();
            try
            {
                Directory.CreateDirectory(dir);
                File.SetUnixFileMode(dir, OwnerOnlyDirectory | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                var leftover = Path.Combine(dir, ExecutionEnvelope.FileName);
                File.WriteAllText(leftover, """{"stale":true}""");
                File.SetUnixFileMode(leftover,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

                var path = ExecutionEnvelope.Write(dir, Valid, (_, _) => { });

                File.ReadAllText(path).Should().Be(Valid);
                File.GetUnixFileMode(path).Should().Be(OwnerAndGroupRead);
                File.GetUnixFileMode(dir).Should().Be(OwnerOnlyDirectory);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void A_refused_envelope_writes_nothing()
        {
            var dir = NewDir();
            try
            {
                var act = () => ExecutionEnvelope.Write(dir, """{"context":{"accessToken":"x"}}""", (_, _) => { });

                act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
                Directory.Exists(dir).Should().BeFalse("screening happens before anything touches the disk");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void The_probe_reports_a_host_that_cannot_hand_off_and_leaves_nothing_behind()
        {
            var dir = NewDir();
            try
            {
                var problem = ExecutionEnvelope.ProbeHandoff(dir,
                    (_, _) => throw new ExecutionEnvelope.HandoffException("not a member of gid 10001"));

                problem.Should().Contain("gid 10001");
                Directory.EnumerateFileSystemEntries(dir).Should().BeEmpty();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void The_probe_passes_when_the_handoff_works_and_leaves_nothing_behind()
        {
            var dir = NewDir();
            try
            {
                ExecutionEnvelope.ProbeHandoff(dir, (_, _) => { }).Should().BeNull();
                Directory.EnumerateFileSystemEntries(dir).Should().BeEmpty();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
