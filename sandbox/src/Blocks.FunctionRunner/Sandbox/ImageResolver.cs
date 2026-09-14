using Blocks.FunctionRunner.Options;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>Makes sure the image for a run is present locally and is the one that was asked for.</summary>
    public interface IImageResolver
    {
        /// <summary>
        /// Returns a reference that can be run, pulling it if absent.
        /// </summary>
        /// <returns>The reference to run, or null when the image cannot be resolved.</returns>
        Task<string?> EnsureAsync(string reference, CancellationToken token);
    }

    /// <inheritdoc cref="IImageResolver"/>
    public sealed class ImageResolver : IImageResolver
    {
        private readonly IDockerClient _docker;
        private readonly RunnerOptions _options;
        private readonly ILogger<ImageResolver> _logger;

        public ImageResolver(IDockerClient docker, IOptions<RunnerOptions> options, ILogger<ImageResolver> logger)
        {
            _docker = docker;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<string?> EnsureAsync(string reference, CancellationToken token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference);

            // Present already? Nothing to do. Digest-pinned references are content-addressed, so
            // a local hit is a guarantee, not an optimisation.
            try
            {
                await _docker.Images.InspectImageAsync(reference, token).ConfigureAwait(false);
                return reference;
            }
            catch (DockerImageNotFoundException)
            {
                // Fall through and pull.
            }
            catch (DockerApiException ex)
            {
                _logger.LogWarning("Inspecting image {Reference} failed: {Message}", reference, ex.Message);
            }

            var (name, tag) = SplitReference(reference);
            _logger.LogInformation("Pulling image {Reference}", reference);

            try
            {
                await _docker.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = name, Tag = tag },
                    authConfig: null,
                    new Progress<JSONMessage>(),
                    token).ConfigureAwait(false);
            }
            catch (DockerApiException ex)
            {
                _logger.LogError("Could not pull image {Reference}: {Message}", reference, ex.Message);
                return null;
            }

            // Verify it actually arrived rather than trusting the pull's exit.
            try
            {
                await _docker.Images.InspectImageAsync(reference, token).ConfigureAwait(false);
                return reference;
            }
            catch (DockerApiException ex)
            {
                _logger.LogError("Image {Reference} is still absent after pulling: {Message}", reference, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Splits a reference into the parts Docker's create-image API wants. A digest reference
        /// keeps its <c>@sha256:…</c> as the "tag", which is how the Engine pins by content.
        /// </summary>
        internal static (string Name, string Tag) SplitReference(string reference)
        {
            var at = reference.IndexOf('@', StringComparison.Ordinal);
            if (at > 0) return (reference[..at], reference[(at + 1)..]);

            // A colon is only a tag separator when it comes after the last slash; otherwise it
            // is a registry port, as in 127.0.0.1:5000/fn/x.
            var lastSlash = reference.LastIndexOf('/');
            var colon = reference.LastIndexOf(':');
            return colon > lastSlash
                ? (reference[..colon], reference[(colon + 1)..])
                : (reference, "latest");
        }
    }
}
