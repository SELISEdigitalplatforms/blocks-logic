using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A run that carries an artifact is a run this host can serve on its own — it builds the image
    /// from the artifact and the shared base instead of asking a registry for what another host made.
    /// What these pin is that the two paths never blur into each other.
    /// </summary>
    public class ArtifactResolutionTests : IAsyncLifetime
    {
        private DockerClient? _docker;

        public async Task InitializeAsync()
        {
            try
            {
                _docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
                await _docker.System.PingAsync();
            }
            catch (Exception)
            {
                _docker = null;
            }
        }

        public Task DisposeAsync()
        {
            _docker?.Dispose();
            return Task.CompletedTask;
        }

        private sealed class BuilderSpy : IArtifactImageBuilder
        {
            public int Calls { get; private set; }
            public string? LastUrl { get; private set; }
            public string? LastSha { get; private set; }
            public bool Result { get; init; } = true;

            public Task<bool> BuildAsync(
                string reference, string artifactUrl, string? expectedSha256, CancellationToken token)
            {
                Calls++;
                LastUrl = artifactUrl;
                LastSha = expectedSha256;
                return Task.FromResult(Result);
            }
        }

        /// <summary>
        /// The real daemon, so the first step — "is this image already here?" — answers the way it
        /// does in production. The references below are made up, so it answers no and the resolver
        /// goes on to decide between the artifact and a pull, which is the part under test.
        /// </summary>
        private ImageResolver Resolver(IArtifactImageBuilder? builder)
        {
            var options = Microsoft.Extensions.Options.Options.Create(
                new RunnerOptions { Registry = "127.0.0.1:5000" });

            return new ImageResolver(_docker!, options, NullLogger<ImageResolver>.Instance, builder);
        }

        [SkippableFact]
        public async Task An_artifact_is_built_here_rather_than_pulled()
        {
            Skip.If(_docker is null, "no Docker available");
            var builder = new BuilderSpy();

            var resolved = await Resolver(builder)
                .EnsureAsync("blocks-fn/f1:abc", CancellationToken.None, "https://store/a.tar", "deadbeef");

            resolved.Should().Be("blocks-fn/f1:abc");
            builder.Calls.Should().Be(1);
            builder.LastUrl.Should().Be("https://store/a.tar");
            builder.LastSha.Should().Be("deadbeef", "the hash has to reach the thing that checks it");
        }

        /// <summary>
        /// The whole reason the artifact exists is that this host may not be able to pull. Falling
        /// back would report a registry miss as the reason a run failed, when the real reason was the
        /// artifact — and on a fleet with no registry at all it would simply never work.
        /// </summary>
        [SkippableFact]
        public async Task A_failed_artifact_build_never_falls_back_to_pulling()
        {
            Skip.If(_docker is null, "no Docker available");
            var builder = new BuilderSpy { Result = false };

            var resolved = await Resolver(builder)
                .EnsureAsync("blocks-fn/f1:abc", CancellationToken.None, "https://store/a.tar", "deadbeef");

            resolved.Should().BeNull();
            builder.Calls.Should().Be(1);
        }

        /// <summary>
        /// A run carrying an artifact on a runner with no builder wired up is a wiring mistake. The
        /// safe reading is "cannot run this", not "pull something instead".
        /// </summary>
        [SkippableFact]
        public async Task An_artifact_with_no_builder_wired_up_fails_rather_than_pulling()
        {
            Skip.If(_docker is null, "no Docker available");
            var resolved = await Resolver(builder: null)
                .EnsureAsync("blocks-fn/f1:abc", CancellationToken.None, "https://store/a.tar", "deadbeef");

            resolved.Should().BeNull();
        }

        /// <summary>
        /// The old path, untouched — which is what lets a control plane that predates artifacts keep
        /// working against this runner. Whether the pull itself succeeds is not the point; that the
        /// artifact builder was never consulted is.
        /// </summary>
        [SkippableFact]
        public async Task A_run_without_an_artifact_does_not_touch_the_builder()
        {
            Skip.If(_docker is null, "no Docker available");
            var builder = new BuilderSpy();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await Resolver(builder).EnsureAsync("127.0.0.1:5000/fn/not-there:1", cts.Token);
            }
            catch (Exception)
            {
                // A registry that is not there is fine here.
            }

            builder.Calls.Should().Be(0);
        }

        // ---- the work directory an artifact is unpacked in ---------------------------

        /// <summary>
        /// An image reference carries <c>/</c>, <c>:</c> and <c>@</c>. The first would put the work
        /// directory somewhere else entirely, which is a path-traversal bug wearing a different hat.
        /// </summary>
        [Theory]
        [InlineData("blocks-fn/f1:abc", "blocks-fn_f1_abc")]
        [InlineData("reg:5000/fn/x@sha256:aa", "reg_5000_fn_x_sha256_aa")]
        [InlineData("../../etc/passwd", ".._.._etc_passwd")]
        public void A_reference_cannot_escape_its_work_directory(string reference, string expected)
        {
            ArtifactImageBuilder.Sanitize(reference).Should().Be(expected);
        }

        [Fact]
        public void Two_references_never_share_a_work_directory()
        {
            ArtifactImageBuilder.Sanitize("blocks-fn/f1:a")
                .Should().NotBe(ArtifactImageBuilder.Sanitize("blocks-fn/f1:b"));
        }
    }
}
