using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Blocks.FunctionRunner.Options;

namespace Blocks.FunctionRunner.Maintenance
{
    /// <summary>Deletes manifests from the runner's local image registry.</summary>
    public interface IRegistryClient
    {
        /// <summary>
        /// Removes the manifest a repo digest names. True when the registry no longer holds it —
        /// including when it never did, since the goal is absence rather than an event.
        /// </summary>
        Task<bool> DeleteManifestAsync(string repoDigest, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The registry is a second copy of every image, and deleting one from the Docker daemon
    /// leaves the other behind — that copy is the one a sandbox actually pulls from. Nothing was
    /// reclaiming it, so a host could prune its image store to nothing and still fill its disk.
    /// <para>
    /// Deletion is by manifest digest, which is what the daemon reports in <c>RepoDigests</c>, so
    /// no tag lookup is needed. The registry must run with <c>REGISTRY_STORAGE_DELETE_ENABLED</c>;
    /// where it does not, deletes come back 405 and are logged once rather than retried forever —
    /// blob storage is then an operator's problem (<c>registry garbage-collect</c>), not a
    /// correctness one.
    /// </para>
    /// </summary>
    public sealed class RegistryClient : IRegistryClient
    {
        private readonly HttpClient _http;
        private readonly RunnerOptions _options;
        private readonly ILogger<RegistryClient> _logger;
        private bool _deleteUnsupportedLogged;

        public RegistryClient(HttpClient http, IOptions<RunnerOptions> options, ILogger<RegistryClient> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<bool> DeleteManifestAsync(string repoDigest, CancellationToken cancellationToken = default)
        {
            if (!TryParse(repoDigest, _options.Registry, out var name, out var digest))
            {
                return false;
            }

            var url = $"http://{_options.Registry}/v2/{name}/manifests/{digest}";
            try
            {
                using var response = await _http.DeleteAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK or HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("Removed {Name}@{Digest} from the registry", name, Short(digest));
                    return true;
                }

                if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.Unauthorized)
                {
                    if (!_deleteUnsupportedLogged)
                    {
                        _deleteUnsupportedLogged = true;
                        _logger.LogWarning(
                            "The registry refused a manifest delete ({Status}); image blobs will not be " +
                            "reclaimed until it runs with storage delete enabled", (int)response.StatusCode);
                    }
                    return false;
                }

                _logger.LogWarning(
                    "Registry delete of {Name}@{Digest} returned {Status}", name, Short(digest), (int)response.StatusCode);
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // The local image is already gone by this point; the registry copy can wait for
                // the next sweep rather than failing one.
                _logger.LogWarning("Registry delete of {Name} failed: {Message}", name, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Splits <c>127.0.0.1:5000/fn/abc@sha256:…</c> into the repository name and the digest.
        /// Only references served by this runner's own registry are accepted: deleting from
        /// anywhere else is not this process's business, and a reference without a digest names
        /// no single manifest to remove.
        /// </summary>
        internal static bool TryParse(string? repoDigest, string registry, out string name, out string digest)
        {
            name = string.Empty;
            digest = string.Empty;
            if (string.IsNullOrWhiteSpace(repoDigest) || string.IsNullOrWhiteSpace(registry)) return false;

            var at = repoDigest.IndexOf('@', StringComparison.Ordinal);
            if (at <= 0 || at == repoDigest.Length - 1) return false;

            var repository = repoDigest[..at];
            var candidate = repoDigest[(at + 1)..];
            if (!candidate.StartsWith("sha256:", StringComparison.Ordinal)) return false;

            var prefix = registry + "/";
            if (!repository.StartsWith(prefix, StringComparison.Ordinal)) return false;

            var path = repository[prefix.Length..];
            if (path.Length == 0 || path.Contains("..", StringComparison.Ordinal)) return false;

            name = path;
            digest = candidate;
            return true;
        }

        private static string Short(string digest) =>
            digest.Length > 19 ? digest[..19] + "…" : digest;
    }
}
