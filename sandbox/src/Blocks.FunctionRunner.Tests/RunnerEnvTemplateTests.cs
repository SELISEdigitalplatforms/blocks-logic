using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The two runner.env templates must describe the same file.
    /// <para>
    /// <c>provision/40-runner-user.sh</c> writes the runner.env that actually runs;
    /// <c>deploy/runner.env.example</c> is the reference an operator reads. They are separate
    /// files by necessity — one is a heredoc inside a provisioning script, the other is
    /// documentation — and they drifted apart once already, quietly: the example documented
    /// thirty keys and the thing that ran wrote fourteen, so half of what the reference promised
    /// was never on any host. A comment asking the next person to keep them in step is what
    /// failed the first time, so this asserts it instead.
    /// </para>
    /// <para>
    /// Only the key <i>names</i> are compared, commented-out lines included. Values differ on
    /// purpose — the example shows a filled-in host, the template ships blanks and defaults.
    /// </para>
    /// </summary>
    public sealed class RunnerEnvTemplateTests
    {
        private static readonly Regex KeyLine = new(
            @"^#?\s*([A-Za-z_][A-Za-z0-9_]*)=", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Walks up from the test assembly to the sandbox root. Located by content rather than by
        /// a fixed number of "../" steps, so moving the test project does not silently turn this
        /// into a test that skips.
        /// </summary>
        private static string? SandboxRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "deploy", "runner.env.example")) &&
                    File.Exists(Path.Combine(dir.FullName, "provision", "40-runner-user.sh")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }

        private static HashSet<string> KeysIn(string text) =>
            KeyLine.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// The heredoc body only. The script's own shell variables — RUNNER_USER, APP_DIR and the
        /// rest — are assignments too, and counting them would compare the script with the
        /// reference rather than the file the script writes.
        /// </summary>
        internal static string HeredocBody(string script)
        {
            var start = script.IndexOf("<<ENV", StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("40-runner-user.sh no longer writes runner.env from a <<ENV heredoc");

            var body = script[(start + "<<ENV".Length)..];
            var end = body.IndexOf("\nENV\n", StringComparison.Ordinal);
            if (end < 0) throw new InvalidOperationException("the <<ENV heredoc in 40-runner-user.sh is not terminated");

            return body[..end];
        }

        [SkippableFact]
        public void The_provisioned_template_and_the_documented_example_declare_the_same_keys()
        {
            var root = SandboxRoot();
            Skip.If(root is null, "not running from a source checkout");

            var example = KeysIn(File.ReadAllText(Path.Combine(root!, "deploy", "runner.env.example")));
            var template = KeysIn(HeredocBody(File.ReadAllText(Path.Combine(root!, "provision", "40-runner-user.sh"))));

            // Both directions, and named separately: "the example documents a key no host gets"
            // and "a host gets a key nobody documented" are different mistakes with different
            // fixes, and a set-equality failure would not say which had happened.
            template.Except(example).Should().BeEmpty(
                "every key provision/40-runner-user.sh writes must be documented in deploy/runner.env.example");
            example.Except(template).Should().BeEmpty(
                "every key deploy/runner.env.example documents must appear in the template provision/40-runner-user.sh writes");
        }

        [SkippableFact]
        public void The_provisioned_template_never_ships_a_credential()
        {
            var root = SandboxRoot();
            Skip.If(root is null, "not running from a source checkout");

            var body = HeredocBody(File.ReadAllText(Path.Combine(root!, "provision", "40-runner-user.sh")));

            // A filled-in secret committed here would be on every host this ever provisions.
            // The secret-bearing keys must be present but empty or commented out.
            foreach (var line in body.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('#')) continue;

                foreach (var secret in new[]
                         {
                             "BlocksSecret__CacheConnectionString", "BlocksSecret__DatabaseConnectionString",
                             "BlocksSecret__LogConnectionString", "RUNNER__RegistryPassword",
                             "KeyVault__ClientSecret",
                         })
                {
                    if (!trimmed.StartsWith(secret + "=", StringComparison.Ordinal)) continue;

                    trimmed[(secret.Length + 1)..].Should().BeEmpty(
                        $"'{secret}' must ship empty; provisioning must never write a credential");
                }
            }
        }

        [SkippableFact]
        public void The_runtime_is_pinned_to_runsc_in_what_provisioning_writes()
        {
            var root = SandboxRoot();
            Skip.If(root is null, "not running from a source checkout");

            var body = HeredocBody(File.ReadAllText(Path.Combine(root!, "provision", "40-runner-user.sh")));

            // Not a tuning knob: a template that shipped anything else would leave every host it
            // provisions healthy-looking and refusing all work.
            body.Should().Contain(
                $"RUNNER__Runtime={Blocks.FunctionRunner.Contracts.Ceilings.SandboxRuntime}",
                "the template must pin the sandbox runtime to the one compiled-in value");
        }
    }
}
