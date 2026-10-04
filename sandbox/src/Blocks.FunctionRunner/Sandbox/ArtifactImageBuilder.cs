using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Blocks.FunctionRunner.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>Turns a run's build artifact into a local image this host can run.</summary>
    public interface IArtifactImageBuilder
    {
        /// <summary>
        /// Downloads the artifact at <paramref name="artifactUrl"/>, checks it against
        /// <paramref name="expectedSha256"/>, and builds <paramref name="reference"/> from it.
        /// <para>
        /// Returns false when the artifact could not be fetched, did not match, or would not build —
        /// never when it merely took a while. The caller reports that as a failed run; it must not
        /// fall back to running something else.
        /// </para>
        /// </summary>
        Task<bool> BuildAsync(
            string reference, string artifactUrl, string? expectedSha256, CancellationToken token);
    }

    /// <summary>
    /// Builds a function's image on the host that needs it, from an artifact and the shared base.
    /// <para>
    /// This is what replaces pulling from a registry. The artifact is the build context the builder
    /// produced — manifest, the installed dependency tree, and the source — so building it here is
    /// <c>COPY</c> and <c>ADD</c> and nothing else. There is deliberately no <c>npm install</c>: the
    /// install already happened once, inside a gVisor sandbox on the builder, and re-running it here
    /// would both execute tenant-chosen code under <c>runc</c> and let two hosts resolve different
    /// versions of the same dependency.
    /// </para>
    /// <para>
    /// Because of that the build is seconds, and it is paid once per host per version — after which
    /// the local image store answers.
    /// </para>
    /// </summary>
    public sealed class ArtifactImageBuilder : IArtifactImageBuilder
    {
        /// <summary>
        /// One build at a time per image reference.
        /// <para>
        /// Ten runs of a cold function arrive together, and without this every one of them downloads
        /// the same artifact and runs the same build. The first does the work; the rest wait on it
        /// and then find the image already there.
        /// </para>
        /// </summary>
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
            new(StringComparer.Ordinal);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly RunnerOptions _options;
        private readonly ILogger<ArtifactImageBuilder> _logger;

        public const string HttpClientName = "fn-artifacts";

        public ArtifactImageBuilder(
            IHttpClientFactory httpClientFactory,
            IOptions<RunnerOptions> options,
            ILogger<ArtifactImageBuilder> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<bool> BuildAsync(
            string reference, string artifactUrl, string? expectedSha256, CancellationToken token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference);
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactUrl);

            var gate = Locks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var workDir = Path.Combine(_options.ArtifactsDir, Sanitize(reference));
                Directory.CreateDirectory(workDir);

                try
                {
                    var archivePath = Path.Combine(workDir, "context.tar");
                    if (!await DownloadAsync(artifactUrl, archivePath, expectedSha256, token).ConfigureAwait(false))
                    {
                        return false;
                    }

                    var contextDir = Path.Combine(workDir, "context");
                    Directory.CreateDirectory(contextDir);
                    if (!await ExtractAsync(archivePath, contextDir, token).ConfigureAwait(false))
                    {
                        return false;
                    }

                    // The archive is large and the image layer now holds everything in it. Keeping
                    // both would roughly double what this function costs on disk.
                    TryDelete(archivePath);

                    return await DockerBuildAsync(reference, contextDir, token).ConfigureAwait(false);
                }
                finally
                {
                    // The extracted tree is only needed for the build. The image is the artefact
                    // that lasts; this is scratch space and it is reclaimed either way.
                    TryDeleteDirectory(workDir);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<bool> DownloadAsync(
            string url, string destination, string? expectedSha256, CancellationToken token)
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            try
            {
                using var response = await client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // A signed URL that 403s is almost always expired — a run that sat in the queue
                    // longer than the window. Saying which it was saves a long hunt.
                    _logger.LogError(
                        "Downloading the artifact failed with {Status}; the signed URL may have expired",
                        (int)response.StatusCode);
                    return false;
                }

                await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                await using (var file = File.Create(destination))
                {
                    await source.CopyToAsync(file, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                _logger.LogError(ex, "Downloading the artifact failed");
                return false;
            }

            return VerifyHash(destination, expectedSha256);
        }

        /// <summary>
        /// A signature says where bytes came from, not that all of them arrived. A truncated or
        /// swapped artifact would otherwise be built into an image and executed, so a mismatch fails
        /// the run rather than running something nobody deployed.
        /// </summary>
        private bool VerifyHash(string path, string? expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                // Nothing to check against. The control plane always sends one for an artifact it
                // recorded, so this means an entry written before the hash existed.
                _logger.LogWarning("The artifact carries no expected hash; it is used unverified");
                return true;
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));

            if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "The artifact does not match its expected hash (expected {Expected}, got {Actual})",
                    expectedSha256, actual);
                return false;
            }

            return true;
        }

        private async Task<bool> ExtractAsync(string archivePath, string contextDir, CancellationToken token)
        {
            // `tar` rather than a library: the archive was written by `tar` on the builder, and the
            // modes and the .bin symlinks npm creates have to survive the round trip intact.
            var exit = await RunAsync(
                "tar", ["-xf", archivePath, "-C", contextDir], token).ConfigureAwait(false);

            if (exit != 0)
            {
                _logger.LogError("Extracting the artifact failed (tar exited {Exit})", exit);
                return false;
            }

            return true;
        }

        private async Task<bool> DockerBuildAsync(string reference, string contextDir, CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();

            // The CLI rather than the API: the context is already a directory on disk and `docker
            // build` streams it as-is, where the API would want it tarred again.
            var exit = await RunAsync(
                "docker",
                ["build", "--quiet", "--tag", reference, "--file", Path.Combine(contextDir, "Dockerfile"), contextDir],
                token).ConfigureAwait(false);

            if (exit != 0)
            {
                _logger.LogError(
                    "Building {Reference} from its artifact failed (docker exited {Exit})", reference, exit);
                return false;
            }

            _logger.LogInformation(
                "Built {Reference} from its artifact in {Elapsed}ms", reference, stopwatch.ElapsedMilliseconds);
            return true;
        }

        private static async Task<int> RunAsync(string file, string[] arguments, CancellationToken token)
        {
            var info = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info) ?? throw new InvalidOperationException($"could not start {file}");
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return process.ExitCode;
        }

        /// <summary>
        /// An image reference carries <c>/</c>, <c>:</c> and <c>@</c>, none of which belong in a
        /// directory name — and the first of them would put the work directory somewhere else
        /// entirely.
        /// </summary>
        internal static string Sanitize(string reference)
        {
            var cleaned = new char[reference.Length];
            for (var i = 0; i < reference.Length; i++)
            {
                var c = reference[i];
                cleaned[i] = char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '_';
            }

            return new string(cleaned);
        }

        private void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Could not delete {Path}: {Message}", path, ex.Message);
            }
        }

        private void TryDeleteDirectory(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Could not delete {Path}: {Message}", path, ex.Message);
            }
        }
    }
}
