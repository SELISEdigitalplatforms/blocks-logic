using Blocks.FunctionRunner.Builds;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// F-8: a tenant lockfile is installed only after every entry is screened like a manifest
    /// specifier, and every install gets an advisory check whose policy the runner decides.
    /// </summary>
    public sealed class LockfileAndAuditTests
    {
        private const string Good = """
            {"lockfileVersion":3,"packages":{
              "":{"name":"fn","dependencies":{"ky":"^1.7.0"}},
              "node_modules/ky":{"version":"1.7.2","resolved":"https://registry.npmjs.org/ky/-/ky-1.7.2.tgz","integrity":"sha512-abc"},
              "node_modules/a/node_modules/b":{"version":"1.0.0","inBundle":true}
            }}
            """;

        /// <summary>Exactly what npm 11 in the build image wrote for this manifest (2026-10-07).</summary>
        private const string WrittenByNpm = """
            {
              "name": "fn",
              "lockfileVersion": 3,
              "requires": true,
              "packages": {
                "": { "name": "fn", "dependencies": { "minimist": "1.2.5" } },
                "node_modules/minimist": {
                  "version": "1.2.5",
                  "resolved": "https://registry.npmjs.org/minimist/-/minimist-1.2.5.tgz",
                  "integrity": "sha512-FM9nNUYrRBAELZQT3xeZQ7fmMOBg6nWNmJKTcgsJeaLstP/UODVpGsr5OhXhhXg6f+qtJ8uiZ+PUxkDWcgIXLw==",
                  "license": "MIT"
                }
              }
            }
            """;

        [Fact]
        public void A_lockfile_npm_itself_wrote_passes()
        {
            LockfileValidator.Validate(WrittenByNpm).Ok.Should().BeTrue();
        }

        [Fact]
        public void A_lockfile_of_registry_packages_with_hashes_passes()
        {
            LockfileValidator.Validate(Good).Ok.Should().BeTrue();
        }

        [Theory]
        [InlineData("""{"resolved":"https://evil.example/ky.tgz","integrity":"sha512-x"}""", "outside the npm registry")]
        [InlineData("""{"resolved":"git+ssh://git@github.com/x/ky.git","integrity":"sha512-x"}""", "outside the npm registry")]
        [InlineData("""{"resolved":"http://registry.npmjs.org/ky/-/ky-1.tgz","integrity":"sha512-x"}""", "outside the npm registry")]
        [InlineData("""{"resolved":"file:../ky"}""", "outside the npm registry")]
        [InlineData("""{"resolved":"https://registry.npmjs.org/ky/-/ky-1.tgz"}""", "no integrity")]
        [InlineData("""{"version":"1.0.0"}""", "no \"resolved\"")]
        [InlineData("""{"link":true,"resolved":"https://registry.npmjs.org/x"}""", "link")]
        public void An_entry_installed_from_anywhere_but_the_registry_fails_and_is_named(string entry, string reason)
        {
            var json = """{"lockfileVersion":3,"packages":{"":{},"node_modules/ky":""" + entry + "}}";

            var result = LockfileValidator.Validate(json);

            result.Ok.Should().BeFalse();
            result.Reason.Should().Contain("node_modules/ky").And.Contain(reason);
        }

        [Theory]
        [InlineData("""{"lockfileVersion":1,"dependencies":{}}""")]
        [InlineData("""{"lockfileVersion":3}""")]
        [InlineData("""[]""")]
        [InlineData("""not json""")]
        [InlineData("""{"lockfileVersion":3,"packages":{"../outside":{}}}""")]
        [InlineData("""{"lockfileVersion":3,"packages":{"packages/workspace":{}}}""")]
        public void An_old_malformed_or_escaping_lockfile_fails(string json)
        {
            LockfileValidator.Validate(json).Ok.Should().BeFalse();
        }

        [Fact]
        public void A_big_lockfile_does_not_use_up_the_source_budget()
        {
            var files = new List<SourceFile>
            {
                new("index.js", "export default async () => 1;"),
                new("package.json", """{"type":"module"}"""),
                new("package-lock.json", new string(' ', (int)SourceValidator.MaxSourceBytes + 10)),
            };

            SourceValidator.Validate(files, allowScripts: false).Ok.Should().BeTrue();
        }

        [Fact]
        public void The_install_uses_npm_ci_with_a_lockfile_and_install_without_one_and_audits_both()
        {
            var withLock = BuildSandboxProfile.InstallScript("--omit=dev --ignore-scripts", "B", "E", useLockfile: true);
            var without = BuildSandboxProfile.InstallScript("--omit=dev --ignore-scripts", "B", "E");

            withLock.Should().Contain("npm ci --omit=dev --ignore-scripts").And.NotContain("npm install");
            withLock.Should().NotContain("rm -f package-lock.json");
            without.Should().Contain("rm -f package-lock.json").And.Contain("npm install --omit=dev --ignore-scripts");
            foreach (var script in new[] { withLock, without })
            {
                script.Should().Contain("npm audit --omit=dev --json 2>/dev/null || true")
                    .And.Contain("B-audit").And.Contain("E-audit");
            }
        }

        private static string Log(string auditJson) =>
            "added 3 packages\nB\n{\"dependencies\":{}}\nE\nB-audit\n" + auditJson + "\nE-audit\n";

        private const string Findings = """
            {"auditReportVersion":2,"vulnerabilities":{
              "lodash":{"name":"lodash","severity":"critical"},
              "axios":{"name":"axios","severity":"high"},
              "qs":{"name":"qs","severity":"moderate"}}}
            """;

        [Fact]
        public void Advisories_are_read_from_the_fenced_block_by_severity()
        {
            var report = NpmAudit.Parse(Log(Findings), "B-audit", "E-audit");

            report!.Count("critical").Should().Be(1);
            report.Count("high").Should().Be(1);
            report.Count("moderate").Should().Be(1);
            NpmAudit.Summary(report).Should().Contain("1 critical (lodash)").And.Contain("1 high (axios)");
        }

        [Theory]
        [InlineData("critical", true)]
        [InlineData("high", true)]
        [InlineData("none", false)]
        public void A_critical_advisory_fails_the_build_unless_the_policy_is_off(string level, bool fails)
        {
            var report = NpmAudit.Parse(Log(Findings), "B-audit", "E-audit");

            var failure = NpmAudit.Failure(report, level);

            if (fails) failure.Should().Contain("lodash");
            else failure.Should().BeNull();
        }

        [Fact]
        public void Advisories_below_the_level_only_warn()
        {
            var report = NpmAudit.Parse(Log("""{"vulnerabilities":{"qs":{"severity":"moderate"}}}"""), "B-audit", "E-audit");

            NpmAudit.Failure(report, "critical").Should().BeNull();
            NpmAudit.Summary(report).Should().Contain("1 moderate (qs)");
        }

        [Theory]
        [InlineData("""{"error":{"code":"ENOTFOUND","summary":"registry unreachable"}}""")]
        [InlineData("""garbage""")]
        public void A_check_that_could_not_run_does_not_fail_the_build_and_says_so(string auditOutput)
        {
            var report = NpmAudit.Parse(Log(auditOutput), "B-audit", "E-audit");

            report.Should().BeNull();
            NpmAudit.Failure(report, "critical").Should().BeNull();
            NpmAudit.Summary(report).Should().Contain("could not run");
        }

        [Fact]
        public void The_audit_block_is_removed_from_the_log_a_tenant_sees()
        {
            var shown = BuildProcessor.StripPackagesBlock(
                BuildProcessor.StripPackagesBlock(Log(Findings), "B-audit", "E-audit"), "B", "E");

            shown.Should().NotContain("lodash").And.NotContain("-audit").And.Contain("added 3 packages");
        }

        [Theory]
        [InlineData("critical", true)]
        [InlineData("none", true)]
        [InlineData("bogus", false)]
        [InlineData(null, false)]
        public void Only_known_levels_are_accepted(string? level, bool valid)
        {
            NpmAudit.IsValidLevel(level).Should().Be(valid);
        }
    }
}
