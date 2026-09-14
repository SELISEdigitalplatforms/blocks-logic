using System.Text.Json;
using Blocks.FunctionRunner.Builds;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// What a build installs, and what it says it installed.
    /// <para>
    /// Dependencies are resolved fresh from package.json on every build — no lockfile is accepted
    /// from a tenant or carried between builds. Two things follow, and both are pinned here. A
    /// lockfile must never reach the build context, or it would silently pin the install to
    /// versions the manifest screening never saw. And because a range is only a request, the
    /// build record has to carry the versions npm actually resolved, read back from the fenced
    /// block the build prints, rather than the ranges the manifest asked for.
    /// </para>
    /// </summary>
    public class BuildPackagesTests
    {
        private const string Nonce = "0123456789abcdef0123456789abcdef";
        private static string Begin => BuildProcessor.PackagesMarker(Nonce, "begin");
        private static string End => BuildProcessor.PackagesMarker(Nonce, "end");

        private static string Fenced(string body) => $"npm install output\n{Begin}\n{body}\n{End}\n";

        private static List<(string name, string? version)> Parse(string? json)
        {
            json.Should().NotBeNull();
            using var doc = JsonDocument.Parse(json!);
            return [.. doc.RootElement.EnumerateArray().Select(e => (
                e.GetProperty("name").GetString()!,
                e.GetProperty("version").ValueKind == JsonValueKind.Null
                    ? null
                    : e.GetProperty("version").GetString()))];
        }

        // ---- the build context ------------------------------------------------------

        private static string NewWorkspace() =>
            Path.Combine(Path.GetTempPath(), "blocks-fn-tests", Guid.NewGuid().ToString("n"));

        private static void InWorkspace(IReadOnlyList<SourceFile> files, Action<string> assert)
        {
            var workspace = NewWorkspace();
            try
            {
                BuildProcessor.PrepareWorkspace(workspace, files);
                assert(workspace);
            }
            finally
            {
                if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
            }
        }

        [Fact]
        public void The_manifest_and_the_entry_point_are_laid_out_where_the_template_expects_them()
        {
            var files = new List<SourceFile>
            {
                new("index.js", "export default async () => 1;"),
                new("package.json", """{"type":"module"}"""),
                new("lib/helper.js", "export const a = 1;"),
            };

            InWorkspace(files, workspace =>
            {
                File.Exists(Path.Combine(workspace, "manifest", "package.json")).Should().BeTrue();
                File.Exists(Path.Combine(workspace, "src", "index.js")).Should().BeTrue();
                File.Exists(Path.Combine(workspace, "src", "lib", "helper.js")).Should().BeTrue();
            });
        }

        [Theory]
        [InlineData("package-lock.json")]
        [InlineData("npm-shrinkwrap.json")]
        [InlineData("yarn.lock")]
        [InlineData("pnpm-lock.yaml")]
        [InlineData("PACKAGE-LOCK.JSON")]
        [InlineData("vendor/package-lock.json")]
        public void A_lockfile_never_reaches_the_build_context(string path)
        {
            // The whole point of the change: npm ci with a lockfile was skipped when none was sent
            // and installed pinned versions when one was, and neither is what this pipeline wants.
            var files = new List<SourceFile>
            {
                new("index.js", "export default async () => 1;"),
                new("package.json", """{"type":"module"}"""),
                new(path, """{"lockfileVersion":3}"""),
            };

            InWorkspace(files, workspace =>
            {
                Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .Should().NotContain(Path.GetFileName(path));
            });
        }

        [Fact]
        public void A_path_that_escapes_the_workspace_is_refused_at_the_file_system_too()
        {
            var files = new List<SourceFile>
            {
                new("index.js", "x"),
                new("package.json", "{}"),
                new("../escape.js", "x"),
            };

            var workspace = NewWorkspace();
            try
            {
                var act = () => BuildProcessor.PrepareWorkspace(workspace, files);
                act.Should().Throw<InvalidOperationException>();
            }
            finally
            {
                if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
            }
        }

        // ---- reading back what was resolved -----------------------------------------

        [Fact]
        public void The_resolved_versions_are_read_out_of_the_fenced_block()
        {
            var log = Fenced("""
                {"name":"function","version":"1.0.0","dependencies":{
                  "decimal.js":{"version":"10.4.3"},
                  "ky":{"version":"1.7.5"},
                  "zod":{"version":"3.23.8"}}}
                """);

            Parse(BuildProcessor.ExtractResolvedPackages(log, Begin, End))
                .Should().Equal(("decimal.js", "10.4.3"), ("ky", "1.7.5"), ("zod", "3.23.8"));
        }

        [Fact]
        public void A_dependency_free_function_records_an_empty_list_rather_than_nothing()
        {
            // "[]" and null mean different things: no dependencies, versus no idea.
            var log = Fenced("""{"name":"function","version":"1.0.0"}""");

            BuildProcessor.ExtractResolvedPackages(log, Begin, End).Should().Be("[]");
        }

        [Fact]
        public void A_dependency_npm_could_not_resolve_is_recorded_without_a_version()
        {
            var log = Fenced("""{"dependencies":{"ky":{"missing":true}}}""");

            Parse(BuildProcessor.ExtractResolvedPackages(log, Begin, End))
                .Should().Equal(("ky", (string?)null));
        }

        [Fact]
        public void A_log_with_no_block_reports_nothing_rather_than_an_empty_list()
        {
            BuildProcessor.ExtractResolvedPackages("npm install output only", Begin, End)
                .Should().BeNull();
        }

        [Fact]
        public void An_unterminated_block_is_not_trusted()
        {
            // The build died mid-print; whatever is there is a fragment.
            var log = $"{Begin}\n{{\"dependencies\":{{\"ky\":";

            BuildProcessor.ExtractResolvedPackages(log, Begin, End).Should().BeNull();
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("[1,2,3]")]
        [InlineData("")]
        public void An_unreadable_block_reports_nothing(string body)
        {
            BuildProcessor.ExtractResolvedPackages(Fenced(body), Begin, End).Should().BeNull();
        }

        [Fact]
        public void The_engines_echo_of_the_run_instruction_is_not_mistaken_for_the_block()
        {
            // The classic builder prints the whole RUN instruction, so both markers appear in the
            // log inside a longer line before the block itself is ever printed.
            var log =
                $"Step 4/8 : RUN set -eux; npm install; echo \"{Begin}\"; npm ls; echo \"{End}\"\n" +
                Fenced("""{"dependencies":{"ky":{"version":"1.7.5"}}}""");

            Parse(BuildProcessor.ExtractResolvedPackages(log, Begin, End))
                .Should().Equal(("ky", "1.7.5"));
        }

        [Fact]
        public void A_block_fenced_with_someone_elses_nonce_is_ignored()
        {
            // A function that opts into lifecycle scripts can write to this log. It cannot know
            // this build's nonce, so a forged block is just text.
            var forged = BuildProcessor.PackagesMarker("deadbeef", "begin");
            var forgedEnd = BuildProcessor.PackagesMarker("deadbeef", "end");
            var log =
                $"{forged}\n{{\"dependencies\":{{\"totally-safe\":{{\"version\":\"9.9.9\"}}}}}}\n{forgedEnd}\n" +
                Fenced("""{"dependencies":{"ky":{"version":"1.7.5"}}}""");

            Parse(BuildProcessor.ExtractResolvedPackages(log, Begin, End))
                .Should().Equal(("ky", "1.7.5"));
        }

        [Fact]
        public void The_recorded_list_is_bounded()
        {
            var deps = string.Join(',', Enumerable.Range(0, BuildProcessor.MaxPackagesRecorded + 50)
                .Select(i => $"\"p{i:D4}\":{{\"version\":\"1.0.0\"}}"));

            var packages = BuildProcessor.ExtractResolvedPackages(
                Fenced($"{{\"dependencies\":{{{deps}}}}}"), Begin, End);

            Parse(packages).Should().HaveCount(BuildProcessor.MaxPackagesRecorded);
        }

        // ---- the log the tenant sees -------------------------------------------------

        [Fact]
        public void The_block_is_stripped_from_the_log_shown_to_the_tenant()
        {
            var log = Fenced("""{"dependencies":{"ky":{"version":"1.7.5"}}}""") + "pushed\n";

            var stripped = BuildProcessor.StripPackagesBlock(log, Begin, End);

            stripped.Should().NotContain(Begin).And.NotContain(End).And.NotContain("1.7.5");
            stripped.Should().Contain("npm install output").And.Contain("pushed");
        }

        [Fact]
        public void The_markers_are_gone_from_the_steps_the_log_keeps()
        {
            // The Engine echoes the whole RUN instruction, markers included, in its Step line.
            // That line is worth keeping; the markers in it are not.
            var log =
                $"Step 5/11 : RUN set -eu; echo \"{Begin}\"; npm ls; echo \"{End}\"\n" +
                Fenced("""{"dependencies":{"ky":{"version":"1.7.5"}}}""");

            var stripped = BuildProcessor.StripPackagesBlock(log, Begin, End);

            stripped.Should().NotContain("blocks-packages");
            stripped.Should().Contain("Step 5/11").And.Contain("npm ls");
        }

        [Fact]
        public void A_log_without_a_block_is_left_alone()
        {
            const string log = "npm error 404 Not Found - GET https://registry.npmjs.org/kyy\n";

            BuildProcessor.StripPackagesBlock(log, Begin, End).Should().Be(log);
        }

        // ---- the fallback ------------------------------------------------------------

        [Fact]
        public void The_fallback_describes_what_the_manifest_asked_for()
        {
            var files = new List<SourceFile>
            {
                new("index.js", "x"),
                new("package.json", """
                    {"type":"module",
                     "dependencies":{"ky":"^1.7.0"},
                     "optionalDependencies":{"sharp":"0.33.0"},
                     "devDependencies":{"vitest":"^1.0.0"}}
                    """),
            };

            // devDependencies are omitted: --omit=dev means they are never installed, so listing
            // them on the build record would misdescribe the image.
            Parse(BuildProcessor.DescribeRequestedPackages(files))
                .Should().Equal(("ky", "^1.7.0"), ("sharp", "0.33.0"));
        }

        [Theory]
        [InlineData("{not json")]
        [InlineData("[]")]
        public void The_fallback_survives_a_manifest_it_cannot_read(string manifest)
        {
            var files = new List<SourceFile> { new("index.js", "x"), new("package.json", manifest) };

            BuildProcessor.DescribeRequestedPackages(files).Should().Be("[]");
        }

        [Fact]
        public void The_fallback_survives_a_bundle_with_no_manifest()
        {
            BuildProcessor.DescribeRequestedPackages([new SourceFile("index.js", "x")]).Should().Be("[]");
        }
    }
}
