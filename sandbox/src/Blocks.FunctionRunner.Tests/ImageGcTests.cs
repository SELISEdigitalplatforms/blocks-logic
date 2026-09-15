using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Options;
using Docker.DotNet;
using Docker.DotNet.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Only the one Image GC behaviour that can be checked without touching the host.
    /// <para>
    /// The rest of Image GC's behaviour — what it prunes and what it refuses to prune — lives in
    /// <c>verify/scenarios/image-gc.sh</c>, not here. Exercising it in <c>dotnet test</c> means
    /// emptying the real <c>functions:images:keep</c> set and sweeping the real image store, which
    /// on a live runner deletes tenant images. It did exactly that on the reference VM before this
    /// was moved out. <c>make test</c> has to be safe to run on a running host.
    /// </para>
    /// </summary>
    public sealed class ImageGcTests : IAsyncLifetime
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

        private bool Unavailable => _docker is null;

        [SkippableFact]
        public async Task Refuses_to_sweep_at_all_when_the_keep_set_cannot_be_read()
        {
            Skip.If(Unavailable, "no Docker available");

            // Without the keep set every image looks unreferenced. Guessing would delete the lot,
            // so the only safe behaviour is to do nothing and say so. A connection to a port
            // nothing listens on gives a genuinely failing IDatabase rather than a stub of one.
            // Safe to run anywhere: it throws before it ever lists an image.
            var dead = ConfigurationOptions.Parse("127.0.0.1:1");
            dead.AbortOnConnectFail = false;
            dead.ConnectTimeout = 300;
            dead.ConnectRetry = 0;
            await using var deadConnection = await ConnectionMultiplexer.ConnectAsync(dead);

            using var gc = new ImageGc(
                _docker!,
                deadConnection.GetDatabase(),
                new RefusingRegistry(),
                Microsoft.Extensions.Options.Options.Create(new RunnerOptions()),
                NullLogger<ImageGc>.Instance);

            var act = async () => await gc.SweepAsync(default);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*refusing to prune*");
        }

        /// <summary>
        /// The base image must survive every sweep, and nothing else may be mistaken for it.
        /// <para>
        /// This was wrong in a way that disabled Image GC entirely rather than loudly: the
        /// repository was taken as everything before the first colon, which for the registry
        /// address <c>127.0.0.1:5000/…</c> is <c>127.0.0.1</c> — a prefix every tenant image on a
        /// host-local registry also carries. Every image looked like the base image, so nothing
        /// was ever pruned and the disk filled up instead.
        /// </para>
        /// </summary>
        [Theory]
        // A registry port is not a tag separator.
        [InlineData("127.0.0.1:5000/blocks/functions-node:24-v1", "127.0.0.1:5000/blocks/functions-node")]
        [InlineData("127.0.0.1:5000/blocks/functions-node", "127.0.0.1:5000/blocks/functions-node")]
        // A digest pin is stripped, tag or no tag.
        [InlineData("127.0.0.1:5000/blocks/functions-node@sha256:abc", "127.0.0.1:5000/blocks/functions-node")]
        [InlineData("registry.internal:5000/blocks/functions-node:24-v1@sha256:abc", "registry.internal:5000/blocks/functions-node")]
        // And a reference with no registry at all still parses.
        [InlineData("blocks-functions-node:24-v1", "blocks-functions-node")]
        public void The_repository_of_a_reference_ignores_the_registry_port(string reference, string expected)
        {
            ImageGc.RepositoryOf(reference).Should().Be(expected);
        }

        [Theory]
        // The tenant images that used to be mistaken for the base image, and so never pruned.
        [InlineData("127.0.0.1:5000/fn/abc123:latest")]
        [InlineData("127.0.0.1:5000/blocks/functions-node-something:1")]
        public void A_tenant_image_is_not_the_base_image(string tenantReference)
        {
            var baseRepo = ImageGc.RepositoryOf("127.0.0.1:5000/blocks/functions-node:24-v1");
            ImageGc.RepositoryOf(tenantReference).Should().NotBe(baseRepo);
        }

        /// <summary>Deleting from the registry is as destructive as deleting locally; a sweep that
        /// refuses to prune must not reach this either.</summary>
        private sealed class RefusingRegistry : Blocks.FunctionRunner.Maintenance.IRegistryClient
        {
            public Task<bool> DeleteManifestAsync(string repoDigest, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("the registry must not be touched by a refused sweep");
        }
    }
}
