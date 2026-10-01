using System.Net;
using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The two halves of "the image is not there", which between them produced every
    /// IMAGE_PULL_FAILED seen on the reference VM: a digest that was published without ever
    /// reaching the registry, and a pull that gave up on its first hiccup.
    /// </summary>
    public sealed class ImagePullResilienceTests : IAsyncLifetime
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

        // ---- what a build is allowed to publish as a digest --------------------------

        [Fact]
        public void The_digest_published_is_the_one_for_the_repository_that_was_pushed()
        {
            // An image identical to another function's is the same image to the daemon, and
            // carries both repositories' references. Taking the first would pin this function's
            // version to a digest served under the other function's name, where no pull of
            // *this* repository can ever find it.
            var digests = new List<string>
            {
                "127.0.0.1:5000/fn/other@sha256:" + new string('a', 64),
                "127.0.0.1:5000/fn/mine@sha256:" + new string('b', 64),
            };

            BuildProcessor.SelectPushedDigest(digests, "127.0.0.1:5000/fn/mine")
                .Should().Be("127.0.0.1:5000/fn/mine@sha256:" + new string('b', 64));
        }

        [Fact]
        public void No_digest_for_the_pushed_repository_is_a_failure_not_an_image_id()
        {
            // The old code fell back to inspect.ID here. With the containerd image store the
            // daemon assigns a manifest digest locally, before any push, so that fallback
            // published a real-looking sha256 the registry had never received — and every run of
            // the version that pinned it failed with "manifest unknown", for good.
            BuildProcessor.SelectPushedDigest(
                ["127.0.0.1:5000/fn/other@sha256:" + new string('a', 64)],
                "127.0.0.1:5000/fn/mine").Should().BeNull();

            BuildProcessor.SelectPushedDigest(null, "127.0.0.1:5000/fn/mine").Should().BeNull();
            BuildProcessor.SelectPushedDigest([], "127.0.0.1:5000/fn/mine").Should().BeNull();
        }

        // ---- when a pull is worth trying again ---------------------------------------

        [Fact]
        public void A_registry_that_says_it_does_not_have_the_image_is_not_asked_again()
        {
            // 404 is an answer, not a failure. Asking twice wastes a run's time budget to be told
            // the same thing.
            ImageResolver.IsAbsentFromRegistry(new DockerApiException(HttpStatusCode.NotFound, "manifest unknown"))
                .Should().BeTrue();
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.RequestTimeout)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        public void Anything_that_might_pass_is_retried(HttpStatusCode status)
        {
            ImageResolver.IsAbsentFromRegistry(new DockerApiException(status, "transient"))
                .Should().BeFalse();

            // The run's own retry policy defaults to a single attempt, so without this the
            // control plane's "IMAGE_PULL_FAILED is retryable" never actually retried anything.
            ImageResolver.PullAttempts.Should().BeGreaterThan(1);
        }

        // ---- the real thing ----------------------------------------------------------

        [SkippableFact]
        public async Task A_digest_the_registry_never_received_resolves_to_null_rather_than_hanging()
        {
            Skip.If(_docker is null, "no Docker available");

            // The exact shape of the reported bug: a reference whose repository exists but whose
            // digest was never pushed. Read-only — it pulls nothing and deletes nothing.
            var options = Microsoft.Extensions.Options.Options.Create(
                new RunnerOptions { Registry = "127.0.0.1:5000" });
            var resolver = new ImageResolver(_docker!, options, NullLogger<ImageResolver>.Instance);

            var missing = "127.0.0.1:5000/fn/does-not-exist@sha256:" + new string('c', 64);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var resolved = await resolver.EnsureAsync(missing, cts.Token);

            resolved.Should().BeNull();
            cts.IsCancellationRequested.Should().BeFalse(
                "a 404 must be answered at once, not retried until the deadline");
        }
    }
}
