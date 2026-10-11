using FluentAssertions;
using Functions.DomainService.Models;
using Functions.DomainService.Services;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The files a build is made from.
    /// <para>
    /// This is where the "Cannot find package" class of failure came from: the builder's image
    /// template ran <c>npm ci</c> only when a lockfile was present, and nothing in the product
    /// ever produced one — the editor has no lockfile tab and the save API leaves the field null.
    /// So every build took the other branch, installed nothing, and still reported SUCCEEDED; the
    /// first <c>import</c> at run time was the first sign anything was wrong. Dependencies are now
    /// resolved fresh from package.json on the runner, and no lockfile is sent at all.
    /// </para>
    /// </summary>
    public class FunctionBuildFileMapTests
    {
        private static FunctionSource Source(string? lockJson = null) => new()
        {
            IndexJs = "import ky from \"ky\";\nexport default async () => 1;",
            PackageJson = """{"type":"module","dependencies":{"ky":"^1.7.0"}}""",
            LockJson = lockJson,
        };

        [Fact]
        public void A_build_is_made_from_the_entry_point_and_the_manifest()
        {
            var files = FunctionBuildService.BuildFileMap(Source());

            files.Keys.Should().BeEquivalentTo("index.js", "package.json");
            files["index.js"].Should().Be(Source().IndexJs);
            files["package.json"].Should().Be(Source().PackageJson);
        }

        [Fact]
        public void A_stored_lockfile_is_sent_for_the_runner_to_screen_and_install_exactly()
        {
            // F-8: the runner screens every entry and runs npm ci, so the build is pinned. The
            // old failure (no install at all without a lockfile) cannot return: the runner always
            // installs, with npm ci or npm install.
            var files = FunctionBuildService.BuildFileMap(Source("""{"lockfileVersion":3}"""));

            files["package-lock.json"].Should().Be("""{"lockfileVersion":3}""");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void Without_a_lockfile_only_the_entry_point_and_manifest_are_sent(string? lockJson)
        {
            FunctionBuildService.BuildFileMap(Source(lockJson)).Keys.Should().BeEquivalentTo("index.js", "package.json");
        }

        [Fact]
        public void The_manifest_is_sent_verbatim_so_the_builder_resolves_what_the_tenant_wrote()
        {
            var source = Source();
            source.PackageJson = """{"type":"module","dependencies":{"zod":"^3.23.8","decimal.js":"^10.4.3"}}""";

            FunctionBuildService.BuildFileMap(source)["package.json"].Should().Be(source.PackageJson);
        }
    }
}
