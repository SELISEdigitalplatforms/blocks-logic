using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Maintenance
{
    /// <summary>
    /// Prunes function images the platform no longer references.
    /// <para>
    /// Every build writes a digest-pinned image and adds that digest to the Redis set
    /// <c>functions:images:keep</c>, which the control plane maintains as the set of digests any
    /// live or recent version still points at. Anything labelled as ours and absent from that set
    /// is, by definition, unreachable — no version can name it, so no run can ask for it.
    /// </para>
    /// <para>
    /// Three things are never pruned, and the order of the checks matters:
    /// <list type="number">
    /// <item>an image any container currently uses — removing it under a running sandbox is the
    /// one mistake here that would be visible to a tenant;</item>
    /// <item>the configured base image, which every future build needs;</item>
    /// <item>anything younger than a grace period, because a freshly built image is briefly
    /// unreferenced while the control plane writes its version record — pruning inside that
    /// window would delete an image seconds before it becomes live.</item>
    /// </list>
    /// </para>
    /// <para>
    /// A miscount here costs a rebuild, not correctness: an image pruned too eagerly is pulled or
    /// rebuilt again. The reverse — a full disk — takes the host down, which is why this runs at
    /// all rather than being left to an operator.
    /// </para>
    /// <para>
    /// Pruning an image removes it from the registry as well as from the daemon. They are two
    /// separate copies, and the registry's is the one sandboxes pull from; deleting only the
    /// daemon's left the bytes that actually matter behind for ever.
    /// </para>
    /// </summary>
    public sealed class ImageGc : BackgroundService
    {
        /// <summary>How long a newly built image is protected regardless of references.</summary>
        public static readonly TimeSpan DefaultGrace = TimeSpan.FromHours(6);

        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        /// <summary>Label the builder puts on every tenant image.</summary>
        private const string FunctionImageLabel = "dev.selise.blocks.function";

        private readonly IDockerClient _docker;
        private readonly IDatabase _db;
        private readonly IRegistryClient _registry;
        private readonly RunnerOptions _options;
        private readonly ILogger<ImageGc> _logger;
        private readonly TimeSpan _grace;

        /// <param name="grace">
        /// Overrides <see cref="DefaultGrace"/>. Injectable so the prune path can be exercised in a
        /// test: a Docker image's Created timestamp cannot be moved, so without this the only way
        /// to reach the deleting branch would be to wait six hours.
        /// </param>
        public ImageGc(
            IDockerClient docker,
            IDatabase db,
            IRegistryClient registry,
            IOptions<RunnerOptions> options,
            ILogger<ImageGc> logger,
            TimeSpan? grace = null)
        {
            _docker = docker;
            _db = db;
            _registry = registry;
            _options = options.Value;
            _logger = logger;
            _grace = grace ?? DefaultGrace;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Not at startup: the first sweep waits, so a host that is restarting because it is
            // unhealthy does not also start deleting things.
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Image GC failed: {Message}", ex.Message);
                }

                try
                {
                    await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Runs one sweep. Returns the number of images removed.
        /// <para>Public so <c>fnctl gc</c> can trigger a sweep on demand rather than carrying a
        /// second implementation of "what is safe to prune", which would drift.</para>
        /// </summary>
        public async Task<int> SweepAsync(CancellationToken token)
        {
            var keep = await LoadKeepSetAsync().ConfigureAwait(false);
            var inUse = await LoadImagesInUseAsync(token).ConfigureAwait(false);

            var images = await _docker.Images.ListImagesAsync(new ImagesListParameters
            {
                All = false,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [$"{FunctionImageLabel}=true"] = true },
                },
            }, token).ConfigureAwait(false);

            var removed = 0;
            var cutoff = DateTime.UtcNow - _grace;

            foreach (var image in images)
            {
                if (image.ID is null) continue;

                if (inUse.Contains(image.ID))
                {
                    continue;
                }

                if (image.Created > cutoff)
                {
                    continue;
                }

                if (IsReferenced(image, keep) || IsBaseImage(image))
                {
                    continue;
                }

                var name = image.RepoTags?.FirstOrDefault()
                           ?? image.RepoDigests?.FirstOrDefault()
                           ?? image.ID;

                try
                {
                    await _docker.Images.DeleteImageAsync(
                        image.ID, new ImageDeleteParameters { Force = false, NoPrune = false }, token)
                        .ConfigureAwait(false);
                    removed++;
                    _logger.LogInformation("Pruned unreferenced function image {Image}", name);

                    // Only after the daemon let go of it: if the local delete is refused because a
                    // stopped sandbox still holds it, the registry copy is what the next pull needs.
                    foreach (var repoDigest in image.RepoDigests ?? [])
                    {
                        await _registry.DeleteManifestAsync(repoDigest, token).ConfigureAwait(false);
                    }
                }
                catch (DockerApiException ex)
                {
                    // Almost always "image is being used by stopped container" — a sandbox this
                    // runner has not finished removing. It will be gone by the next sweep.
                    _logger.LogDebug("Could not prune {Image}: {Message}", name, ex.Message);
                }
            }

            if (removed > 0) _logger.LogInformation("Image GC pruned {Count} image(s)", removed);
            return removed;
        }

        private async Task<HashSet<string>> LoadKeepSetAsync()
        {
            var keep = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var member in await _db.SetMembersAsync(RedisKeys.ImagesKeep).ConfigureAwait(false))
                {
                    var value = member.ToString();
                    if (value.Length == 0) continue;

                    keep.Add(value);

                    // References are stored as `repo@sha256:…`; index the bare digest too, so a
                    // match does not depend on which registry host the reference was written with.
                    var at = value.IndexOf('@', StringComparison.Ordinal);
                    if (at >= 0 && at + 1 < value.Length) keep.Add(value[(at + 1)..]);
                }
            }
            catch (RedisException ex)
            {
                // Without the keep set every image looks unreferenced, so refuse to sweep at all.
                throw new InvalidOperationException(
                    "the image keep-set could not be read; refusing to prune anything", ex);
            }

            return keep;
        }

        /// <summary>Image ids any container references, running or merely stopped.</summary>
        private async Task<HashSet<string>> LoadImagesInUseAsync(CancellationToken token)
        {
            var inUse = new HashSet<string>(StringComparer.Ordinal);
            var containers = await _docker.Containers.ListContainersAsync(
                new ContainersListParameters { All = true }, token).ConfigureAwait(false);

            foreach (var container in containers)
            {
                if (container.ImageID is { Length: > 0 }) inUse.Add(container.ImageID);
            }

            return inUse;
        }

        private static bool IsReferenced(ImagesListResponse image, HashSet<string> keep)
        {
            if (keep.Contains(image.ID)) return true;

            if (image.RepoDigests is not null)
            {
                foreach (var digest in image.RepoDigests)
                {
                    if (keep.Contains(digest)) return true;

                    var at = digest.IndexOf('@', StringComparison.Ordinal);
                    if (at >= 0 && at + 1 < digest.Length && keep.Contains(digest[(at + 1)..])) return true;
                }
            }

            if (image.RepoTags is not null)
            {
                foreach (var tag in image.RepoTags)
                {
                    if (keep.Contains(tag)) return true;
                }
            }

            return false;
        }

        private bool IsBaseImage(ImagesListResponse image)
        {
            var baseImage = _options.BaseImage;
            if (string.IsNullOrWhiteSpace(baseImage)) return false;

            // Compare on the repository, not the full reference: the base image is pinned by
            // digest in production and by tag locally, and both must survive.
            var baseRepo = baseImage.Split('@')[0].Split(':')[0];

            if (image.RepoTags is not null)
            {
                foreach (var tag in image.RepoTags)
                {
                    if (tag.StartsWith(baseRepo, StringComparison.Ordinal)) return true;
                }
            }
            if (image.RepoDigests is not null)
            {
                foreach (var digest in image.RepoDigests)
                {
                    if (digest.StartsWith(baseRepo, StringComparison.Ordinal)) return true;
                }
            }

            return false;
        }
    }
}
