using System.Diagnostics;
using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Maintenance;
using Docker.DotNet;
using Docker.DotNet.Models;
using FluentAssertions;
using ICSharpCode.SharpZipLib.Tar;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A test builds on a dependency image kept per tenant and dependency tree, and adds only the
    /// source (FN-14). These pin what decides reuse, how Image GC treats the kept image, and — on a
    /// real Docker Engine — that the two-step image is the same image the template builds.
    /// </summary>
    public sealed class TestDepsImageTests : IAsyncLifetime
    {
        private const string Base = "127.0.0.1:5000/blocks/functions-node:24-v2";
        private const string Manifest = "{\"name\":\"f\",\"dependencies\":{\"x\":\"1.0.0\"}}";

        private static string Ref(string tenant = "t1", string baseImage = Base, int heap = 192, string manifest = Manifest, string sha = "aa") =>
            BuildProcessor.TestDepsImageRef(tenant, baseImage, heap, manifest, sha);

        [Fact]
        public void The_same_inputs_give_the_same_local_name()
        {
            Ref().Should().Be(Ref());
            Ref().Should().StartWith("blocks-test-deps/").And.EndWith(":local");
            Ref().Should().NotContain("127.0.0.1").And.NotContain("@").And.Be(Ref().ToLowerInvariant());
        }

        [Fact]
        public void Anything_that_changes_the_contents_gives_another_image()
        {
            new[]
            {
                Ref(tenant: "t2"),                  // never shared across tenants
                Ref(baseImage: Base + "-next"),     // a new runtime
                Ref(heap: 256),                     // another heap size
                Ref(manifest: Manifest + " "),      // package.json edited
                Ref(sha: "bb"),                     // same manifest, newer versions resolved
            }.Should().OnlyHaveUniqueItems().And.NotContain(Ref());
        }

        [Fact]
        public void The_dependency_image_keeps_the_template_layout_user_heap_and_entrypoint()
        {
            var template = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "function.Dockerfile.tmpl"));
            var deps = BuildProcessor.TestDepsDockerfile(Base, "deps.tar", 192);

            // Every instruction of the template that is not about the source.
            foreach (var line in template.Where(l => l.StartsWith("USER ", StringComparison.Ordinal)
                                                     || l.StartsWith("WORKDIR ", StringComparison.Ordinal)
                                                     || l.StartsWith("ENTRYPOINT ", StringComparison.Ordinal)
                                                     || l.StartsWith("COPY manifest/", StringComparison.Ordinal)))
            {
                deps.Should().Contain(line);
            }
            deps.Should().Contain($"FROM {Base}").And.Contain("ADD deps.tar /function/")
                .And.Contain("ENV NODE_OPTIONS=--max-old-space-size=192")
                .And.Contain("chown -R root:root /function").And.Contain("chmod -R a-w,a+rX /function")
                .And.NotContain("src/");
            deps.TrimEnd().Split('\n')[^1].Should().StartWith("ENTRYPOINT");

            BuildProcessor.TestSourceDockerfile("blocks-test-deps/abc:local")
                .Should().Be("FROM blocks-test-deps/abc:local\nCOPY src/ /function/\n");
        }

        [Fact]
        public void A_dependency_image_is_neither_a_test_image_nor_a_deployed_one()
        {
            var labels = new Dictionary<string, string>
            {
                [BuildProcessor.FunctionImageLabel] = "true",
                [BuildProcessor.TestDepsImageLabel] = "true",
            };

            // Not a test image: those go 30 min after they were built, whether busy or not.
            ImageGc.IsTestImage(labels).Should().BeFalse();
            ImageGc.IsTestDepsImage(labels).Should().BeTrue();
            ImageGc.IsTestDepsImage(new Dictionary<string, string> { [BuildProcessor.FunctionImageLabel] = "true" }).Should().BeFalse();
            ImageGc.IsTestDepsImage(null).Should().BeFalse();
            ImageGc.TestDepsIdle.Should().Be(TimeSpan.FromHours(1));
        }

        [Fact]
        public void The_source_context_is_read_only_and_the_dockerfile_is_not()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"fn-tar-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "src", "lib"));
                File.WriteAllText(Path.Combine(dir, "src", "index.js"), "x");
                File.WriteAllText(Path.Combine(dir, "src", "lib", "a.js"), "y");
                File.WriteAllText(Path.Combine(dir, "Dockerfile"), "FROM x");

                var modes = new Dictionary<string, int>();
                using (var tar = BuildProcessor.CreateTarContext(dir, readOnlySource: true))
                using (var input = new TarInputStream(tar, null))
                {
                    TarEntry? entry;
                    while ((entry = input.GetNextEntry()) is not null) modes[entry.Name] = entry.TarHeader.Mode & 0xFFF;
                }

                modes["src/"].Should().Be(0x16D);
                modes["src/lib/"].Should().Be(0x16D);
                modes["src/index.js"].Should().Be(0x16D);
                modes["src/lib/a.js"].Should().Be(0x16D);
                modes["Dockerfile"].Should().NotBe(0x16D);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // ---- on a real Docker Engine ----------------------------------------------------------

        private DockerClient? _docker;
        private string? _skip;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"fn-deps-{Guid.NewGuid():N}");
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];
        private string RefImage => $"blocks-fn-deps-test/ref-{_suffix}:local";
        private string DepsImage => $"blocks-fn-deps-test/deps-{_suffix}:local";
        private string TestImage => $"blocks-fn-deps-test/test-{_suffix}:local";

        public async Task InitializeAsync()
        {
            try
            {
                _docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
                var images = await _docker.Images.ListImagesAsync(new ImagesListParameters());
                if (!images.SelectMany(i => i.RepoTags ?? []).Contains(Base)) _skip = $"no {Base} image";
            }
            catch (Exception)
            {
                _skip = "no Docker available";
            }
        }

        public async Task DisposeAsync()
        {
            if (_docker is not null)
            {
                foreach (var image in new[] { TestImage, DepsImage, RefImage })
                {
                    try
                    {
                        await _docker.Images.DeleteImageAsync(image, new ImageDeleteParameters { Force = true });
                    }
                    catch (DockerApiException)
                    {
                        // never built
                    }
                }
                _docker.Dispose();
            }
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [SkippableFact]
        public async Task The_two_step_image_is_the_image_the_template_builds()
        {
            Skip.If(_skip is not null, _skip);

            // One source tree, one dependency archive, built both ways.
            var all = Path.Combine(_root, "template");
            Directory.CreateDirectory(Path.Combine(all, "manifest"));
            Directory.CreateDirectory(Path.Combine(all, "src", "lib"));
            File.WriteAllText(Path.Combine(all, "manifest", "package.json"), Manifest);
            File.WriteAllText(Path.Combine(all, "src", "index.js"), "module.exports = async () => 1;");
            File.WriteAllText(Path.Combine(all, "src", "lib", "a.js"), "module.exports = 2;");
            var modules = Path.Combine(_root, "modules");
            Directory.CreateDirectory(Path.Combine(modules, "node_modules", "x"));
            File.WriteAllText(Path.Combine(modules, "node_modules", "x", "index.js"), "module.exports = 3;");
            using (var tar = Process.Start("tar", ["-cf", Path.Combine(all, "deps.tar"), "-C", modules, "node_modules"]))
            {
                await tar.WaitForExitAsync();
                tar.ExitCode.Should().Be(0);
            }

            var template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "function.Dockerfile.tmpl"))
                .Replace("{{BASE_IMAGE}}", Base, StringComparison.Ordinal)
                .Replace("{{DEPS_ARCHIVE}}", "deps.tar", StringComparison.Ordinal)
                .Replace("{{MAX_OLD_SPACE_MB}}", "192", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(all, "Dockerfile"), template);
            // Through the runner's own build context, as a deployed build goes.
            await BuildAsync(all, RefImage, readOnlySource: false);

            var deps = Path.Combine(_root, "deps");
            Directory.CreateDirectory(deps);
            CopyTree(Path.Combine(all, "manifest"), Path.Combine(deps, "manifest"));
            File.Copy(Path.Combine(all, "deps.tar"), Path.Combine(deps, "deps.tar"));
            File.WriteAllText(Path.Combine(deps, "Dockerfile"), BuildProcessor.TestDepsDockerfile(Base, "deps.tar", 192));
            await BuildAsync(deps, DepsImage, readOnlySource: false);

            var test = Path.Combine(_root, "test");
            Directory.CreateDirectory(test);
            CopyTree(Path.Combine(all, "src"), Path.Combine(test, "src"));
            File.WriteAllText(Path.Combine(test, "Dockerfile"), BuildProcessor.TestSourceDockerfile(DepsImage));
            await BuildAsync(test, TestImage, readOnlySource: true);

            (await Describe(TestImage)).Should().Be(await Describe(RefImage));
        }

        private async Task BuildAsync(string context, string tag, bool readOnlySource)
        {
            using var tar = BuildProcessor.CreateTarContext(context, readOnlySource);
            var errors = new List<string>();
            await _docker!.Images.BuildImageFromDockerfileAsync(
                new ImageBuildParameters { Dockerfile = "Dockerfile", Tags = [tag], Remove = true, ForceRemove = true },
                tar, null, null, new Progress<JSONMessage>(m => { if (!string.IsNullOrEmpty(m.ErrorMessage)) errors.Add(m.ErrorMessage); }));
            errors.Should().BeEmpty();
        }

        /// <summary>Modes, owners and paths under /function, plus the config a run depends on.</summary>
        private async Task<string> Describe(string image)
        {
            var files = new System.Text.StringBuilder();
            await Docker(files, "run", "--rm", "--entrypoint", "sh", image, "-c",
                "find /function -exec stat -c '%a %u:%g %n' {} + | sort");
            var inspect = await _docker!.Images.InspectImageAsync(image);
            return $"{files}\n{inspect.Config.User}\n{string.Join(' ', inspect.Config.Env)}\n"
                   + $"{string.Join(' ', inspect.Config.Entrypoint)}\n{inspect.Config.WorkingDir}";
        }

        private static async Task<int> Docker(System.Text.StringBuilder? output, params string[] args)
        {
            using var process = Process.Start(new ProcessStartInfo("docker", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            output?.Append(stdout);
            return process.ExitCode;
        }

        private static void CopyTree(string from, string to)
        {
            foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            }
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
            }
        }
    }
}
