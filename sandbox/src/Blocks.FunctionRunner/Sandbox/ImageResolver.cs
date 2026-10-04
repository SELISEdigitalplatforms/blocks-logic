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
        /// <summary>
        /// How many times a pull that failed for a reason time might fix is tried in total.
        /// <para>
        /// A run carries the tenant's own retry policy, which defaults to a single attempt, so
        /// before this a momentary registry hiccup was a permanently failed run — the
        /// "retryable" classification the control plane gives IMAGE_PULL_FAILED only ever
        /// applies when a tenant configured more than one attempt. Retrying here is about the
        /// pull, not about the function, and belongs to whoever owns the registry connection.
        /// </para>
        /// </summary>
        internal const int PullAttempts = 3;

        private static readonly TimeSpan PullRetryDelay = TimeSpan.FromSeconds(2);

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

            for (var attempt = 1; attempt <= PullAttempts; attempt++)
            {
                _logger.LogInformation(
                    "Pulling image {Reference} (attempt {Attempt} of {Attempts})", reference, attempt, PullAttempts);

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
                    if (IsAbsentFromRegistry(ex))
                    {
                        // The registry answered, and its answer was "I do not have this". Another
                        // attempt asks the same question and gets the same answer.
                        _logger.LogError(
                            "Image {Reference} is not in registry {Registry}: {Message}. {Explanation}",
                            reference, _options.Registry, ex.Message, AbsenceExplanation());
                        return null;
                    }

                    if (attempt == PullAttempts)
                    {
                        _logger.LogError(
                            "Could not pull image {Reference} after {Attempts} attempts: {Message}",
                            reference, PullAttempts, ex.Message);
                        return null;
                    }

                    _logger.LogWarning(
                        "Pull of {Reference} failed ({Message}); retrying in {Delay}",
                        reference, ex.Message, PullRetryDelay);

                    try
                    {
                        await Task.Delay(PullRetryDelay, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }

                    continue;
                }

                // Separate from the pull's own catch: a pull that reported success and left
                // nothing behind is a different fault from one that failed, and saying "not in
                // the registry" about it would send the reader somewhere the problem is not.
                try
                {
                    await _docker.Images.InspectImageAsync(reference, token).ConfigureAwait(false);
                    return reference;
                }
                catch (DockerApiException ex)
                {
                    _logger.LogError(
                        "Image {Reference} is still absent after a pull that reported success: {Message}",
                        reference, ex.Message);
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// True when the registry was reached and reported that it does not hold the reference —
        /// as opposed to a transport, auth or server-side failure, which another attempt might
        /// get past.
        /// </summary>
        internal static bool IsAbsentFromRegistry(DockerApiException ex) =>
            ex.StatusCode is System.Net.HttpStatusCode.NotFound;

        /// <summary>
        /// The one sentence that turns this from a mystery into a diagnosis. A host-local registry
        /// only holds what this host built; if the control plane is shared with other runners, a
        /// run built elsewhere lands here and its image is, correctly and permanently, absent.
        /// </summary>
        private string AbsenceExplanation() =>
            _options.IsRegistryHostLocal
                ? "This registry is private to this host, so it only holds images this host built. "
                  + "If other runners share this control plane, set RUNNER__Registry (and "
                  + "Functions:Registry on the API) to a registry all of them can reach."
                : "The image was never pushed, or has since been deleted from the registry.";

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
