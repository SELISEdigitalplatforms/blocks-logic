using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A redeploy re-tags the build image; if the runner was not restarted, it still points at the
    /// old digest, which this host may have cleaned up. The install pulls it back by digest first,
    /// and a pull that fails ends the build with a clear reason before any sandbox is made.
    /// </summary>
    public sealed class DependencyInstallerImageTests : IDisposable
    {
        private const string BuildImage = "127.0.0.1:5000/blocks/functions-node@sha256:abc";

        // Never reached: a socket that does not exist proves no Docker call happens.
        private readonly DockerClient _docker =
            new DockerClientConfiguration(new Uri("unix:///nonexistent/docker.sock")).CreateClient();

        private readonly string _work = Path.Combine(Path.GetTempPath(), $"fn-install-{Guid.NewGuid():N}");

        public DependencyInstallerImageTests() => Directory.CreateDirectory(_work);

        public void Dispose()
        {
            _docker.Dispose();
            if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true);
        }

        private DependencyInstaller Installer(IImageResolver images) => new(
            _docker,
            Microsoft.Extensions.Options.Options.Create(new RunnerOptions { RunnerId = "test", BuildImage = BuildImage }),
            NullLogger<DependencyInstaller>.Instance,
            images);

        [Fact]
        public async Task A_build_image_that_cannot_be_pulled_fails_the_build_naming_the_image()
        {
            var images = new RecordingResolver(found: false);

            var result = await Installer(images).InstallAsync("b1", _work, false, "<b>", "<e>", CancellationToken.None);

            result.Ok.Should().BeFalse();
            result.Failure.Should().Contain(BuildImage).And.Contain("could not be pulled");
            images.Asked.Should().Equal(BuildImage);
        }

        [Fact]
        public async Task The_build_image_is_made_present_before_the_sandbox_is_created()
        {
            var images = new RecordingResolver(found: true);

            var result = await Installer(images).InstallAsync("b2", _work, false, "<b>", "<e>", CancellationToken.None);

            images.Asked.Should().Equal(BuildImage);
            result.Failure.Should().NotContain("could not be pulled", "the image was there; Docker itself failed later");
        }

        private sealed class RecordingResolver(bool found) : IImageResolver
        {
            public List<string> Asked { get; } = [];

            public Task<string?> EnsureAsync(
                string reference, CancellationToken token, string? artifactUrl = null, string? artifactSha256 = null)
            {
                Asked.Add(reference);
                return Task.FromResult(found ? reference : null);
            }
        }
    }
}
