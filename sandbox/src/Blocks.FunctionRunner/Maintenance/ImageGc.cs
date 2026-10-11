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
        private const string FunctionImageLabel = Builds.BuildProcessor.FunctionImageLabel;

        /// <summary>
        /// A test image lives for one run and is deleted by it. One still here after this long was
        /// left by a runner that died mid-test; it is removed whatever the keep set says, since no
        /// version can ever point at a test image.
        /// </summary>
        public static readonly TimeSpan TestImageMaxAge = TimeSpan.FromMinutes(30);

        /// <summary>
        /// A test's dependency image is kept while that tenant keeps testing with the same
        /// dependencies, and removed after this long unused (last use from the usage log; with no
        /// record, from when it was built). Rebuilding one costs ~10 s on the next test.
        /// </summary>
        public static readonly TimeSpan TestDepsIdle = TimeSpan.FromHours(1);

        private readonly IDockerClient _docker;
        private readonly IDatabase _db;
        private readonly IRegistryClient _registry;
        private readonly IImageUsageLog? _usage;
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
            TimeSpan? grace = null,
            IImageUsageLog? usage = null)
        {
            // Optional so the sweeps that existing tests construct keep compiling. Without it the
            // cap simply does not evict — the keep-set behaviour is unchanged, which is the safe
            // way round for a cache.
            _usage = usage;
            _docker = docker;
            _db = db;
            _registry = registry;
            _options = options.Value;
            _logger = logger;
            _grace = grace ?? DefaultGrace;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Said once, plainly: "the registry keeps filling up" and "images vanish mid-run" are
            // the two failures this setting decides between, and neither is diagnosable from the
            // sweep's own logs, which look identical either way.
            _logger.LogInformation(
                "Image GC: registry {Registry} is treated as {Scope}; manifests {Action} deleted here",
                _options.Registry,
                _options.IsRegistryHostLocal ? "host-local" : "shared",
                _options.ShouldPruneRegistry ? "are" : "are not");

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
            var pruned = new HashSet<string>(StringComparer.Ordinal);
            var cutoff = DateTime.UtcNow - _grace;

            foreach (var image in images)
            {
                if (image.ID is null) continue;

                if (inUse.Contains(image.ID))
                {
                    continue;
                }

                if (IsTestImage(image.Labels))
                {
                    if (!IsStaleTestImage(image.Labels, image.Created, DateTime.UtcNow)) continue;
                }
                else if (IsTestDepsImage(image.Labels))
                {
                    // A running test's own image is its child, so Docker refuses this delete while
                    // one is built on it; the next sweep tries again.
                    var lastUsed = _usage?.LastUsedUtc(image.RepoTags?.FirstOrDefault() ?? image.ID) ?? image.Created;
                    if (lastUsed > DateTime.UtcNow - TestDepsIdle) continue;
                }
                else
                {
                    if (image.Created > cutoff)
                    {
                        continue;
                    }

                    if (IsReferenced(image, keep) || IsBaseImage(image))
                    {
                        continue;
                    }
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
                    pruned.Add(image.ID);
                    _logger.LogInformation("Pruned unreferenced function image {Image}", name);

                    // Only after the daemon let go of it: if the local delete is refused because a
                    // stopped sandbox still holds it, the registry copy is what the next pull needs.
                    //
                    // And only where this host owns the registry. The "still in use" test above is
                    // this host's container list, which is the whole truth for a registry private to
                    // it and one host's view of a shared one. On a shared registry another runner
                    // can be executing the very image being pruned here — an image no deployed
                    // version pins, so the keep set does not protect it either — and deleting the
                    // manifest would take it out from under that run. The local copy still goes:
                    // reclaiming disk here is safe, it is the shared copy that is not ours to drop.
                    if (_options.ShouldPruneRegistry)
                    {
                        foreach (var repoDigest in image.RepoDigests ?? [])
                        {
                            await _registry.DeleteManifestAsync(repoDigest, token).ConfigureAwait(false);
                        }
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
            // Without the ones just pruned: they no longer take room, and evicting them again fails.
            var remaining = images.Where(i => i.ID is null || !pruned.Contains(i.ID)).ToList();
            removed += await EnforceCacheCapAsync(remaining, inUse, token).ConfigureAwait(false);

            return removed;
       }

        /// <summary>
        /// Evicts the coldest function images once this host holds more than it should.
        /// <para>
        /// Unlike the sweep above, this removes images the keep set still references — on purpose.
        /// With an artifact behind it an image is a cache entry, not a store: evicting one costs the
        /// next run of that function a few seconds of rebuild, where keeping every live function on
        /// a host that serves a thousand of them would cost the disk. A host holds what it is busy
        /// with, not everything that exists.
        /// </para>
        /// <para>
        /// Never evicts an image a container is holding, and never the base image — the base is what
        /// every rebuild is built from, so dropping it would make every eviction far more expensive
        /// than it should be.
        /// </para>
        /// </summary>
        private async Task<int> EnforceCacheCapAsync(
            IList<ImagesListResponse> images, HashSet<string> inUse, CancellationToken token)
        {
            if (_usage is null) return 0;

            var budgetBytes = ReadImageBudgetBytes();
            if (_options.MaxCachedImages <= 0 && budgetBytes <= 0) return 0;

            var baseBytes = await ReadBaseImageBytesAsync(token).ConfigureAwait(false);

            var candidates = images
                .Where(i => i.ID is not null && !inUse.Contains(i.ID) && !IsBaseImage(i)
                            && !IsTestImage(i.Labels) && !IsTestDepsImage(i.Labels))
                .Select(i => new
                {
                    Image = i,
                    Reference = i.RepoTags?.FirstOrDefault() ?? i.RepoDigests?.FirstOrDefault() ?? i.ID!,
                })
                .Select(x => new
                {
                    x.Image,
                    x.Reference,
                    // No record means this host has not run it since the log existed, which makes it
                    // the coldest thing here — exactly what should go first.
                    LastUsed = _usage.LastUsedUtc(x.Reference) ?? DateTime.MinValue,
                })
                .OrderBy(x => x.LastUsed)
                .ToList();

            // Everything else on the disk that this sweep will not evict still takes room: the base
            // once, plus what in-use, test and test-dependency images add on top of it.
            var candidateIds = candidates.Select(c => c.Image.ID!).ToHashSet(StringComparer.Ordinal);
            var otherBytes = baseBytes + images
                .Where(i => i.ID is not null && !candidateIds.Contains(i.ID) && !IsBaseImage(i))
                .Sum(i => UniqueBytes(i.Size, baseBytes));

            var excess = EvictionCount(
                candidates.Select(c => UniqueBytes(c.Image.Size, baseBytes)).ToList(),
                otherBytes, budgetBytes, _options.MaxCachedImages);
            if (excess <= 0) return 0;

            _logger.LogInformation(
                "Image cache holds {Held} function images ({HeldMb} MB own layers + {OtherMb} MB base and others), " +
                "over the cap of {Cap} images / {BudgetMb} MB; evicting the {Excess} coldest",
                candidates.Count,
                candidates.Sum(c => UniqueBytes(c.Image.Size, baseBytes)) / 1024 / 1024,
                otherBytes / 1024 / 1024,
                _options.MaxCachedImages > 0 ? _options.MaxCachedImages : "none",
                budgetBytes > 0 ? budgetBytes / 1024 / 1024 : "none",
                excess);

            var evicted = 0;
            foreach (var candidate in candidates.Take(excess))
            {
                try
                {
                    await _docker.Images.DeleteImageAsync(
                        candidate.Image.ID!, new ImageDeleteParameters { Force = false }, token)
                        .ConfigureAwait(false);
                    evicted++;
                }
                catch (DockerApiException ex)
                {
                    // Almost always a stopped container still holding it; the next sweep gets it.
                    _logger.LogDebug(
                        "Could not evict {Reference}: {Message}", candidate.Reference, ex.Message);
                }
            }

            return evicted;
        }

        /// <summary>
        /// How many of the coldest images to evict: the fewest that bring the count under
        /// <paramref name="maxCount"/> and the bytes under <paramref name="budgetBytes"/>, whichever
        /// binds harder. Zero or less for either means that limit is off.
        /// <para>
        /// Bytes, not an image count derived from an average. Function images differ by orders of
        /// magnitude — a handler with no dependencies adds a few KB to the base, one with an SDK
        /// adds hundreds of MB — so an average count evicts small images to make room that big ones
        /// took, or keeps too many big ones.
        /// </para>
        /// </summary>
        /// <param name="coldestFirstBytes">Each evictable image's own bytes, coldest first.</param>
        /// <param name="otherBytes">What stays on the disk regardless: the base and every image this
        /// sweep may not evict.</param>
        internal static int EvictionCount(
            IReadOnlyList<long> coldestFirstBytes, long otherBytes, long budgetBytes, int maxCount)
        {
            var held = coldestFirstBytes.Count;
            var bytes = otherBytes + coldestFirstBytes.Sum();
            var evict = 0;

            while (evict < held
                   && ((maxCount > 0 && held - evict > maxCount)
                       || (budgetBytes > 0 && bytes > budgetBytes)))
            {
                bytes -= coldestFirstBytes[evict];
                evict++;
            }

            return evict;
        }

        /// <summary>
        /// What one function image really adds to the disk: its size minus the base it is built on.
        /// <para>
        /// Docker reports an image's <c>Size</c> with every parent layer included, so the base
        /// (~330 MB) was counted once per image — a function that adds 25 MB looked like 355 MB, and
        /// the cache held about a tenth of what the disk allowed (FN-18, measured 2026-10-07).
        /// </para>
        /// <para>
        /// With the base unknown, or an image no bigger than it (built on an older, larger base),
        /// the full size is used: over-counting evicts a little early and costs a rebuild;
        /// under-counting could fill the disk.
        /// </para>
        /// </summary>
        internal static long UniqueBytes(long imageSize, long baseBytes)
        {
            var size = Math.Max(imageSize, 0);
            return baseBytes > 0 && size > baseBytes ? size - baseBytes : size;
        }

        /// <summary>
        /// The share of the disk images may use, in bytes. Zero when the disk cannot be read:
        /// without a reading there is no honest number, and evicting on a guess would throw away
        /// images for no reason, so only an explicit <c>MaxCachedImages</c> evicts then.
        /// </summary>
        private long ReadImageBudgetBytes()
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(_options.RunsDir) ?? "/");
                return (long)(drive.TotalSize * (_options.ImageDiskPercent / 100.0));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Could not read the image disk: {Message}", ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// The size of the configured base image, which every function image is built on. Zero when
        /// it cannot be found (not pulled yet, Docker error), which makes <see cref="UniqueBytes"/>
        /// fall back to full sizes — the old, cautious count.
        /// <para>
        /// Read from the image <em>list</em>, like the function sizes it is subtracted from. With the
        /// containerd image store, <c>inspect</c> reports a different figure (81 MB where the list
        /// says 330 MB for the same base, measured 2026-10-07), and mixing the two would make every
        /// function image look ~250 MB bigger than it is.
        /// </para>
        /// </summary>
        private async Task<long> ReadBaseImageBytesAsync(CancellationToken token)
        {
            var reference = _options.BaseImage;
            if (string.IsNullOrWhiteSpace(reference)) return 0;
            try
            {
                var all = await _docker.Images.ListImagesAsync(new ImagesListParameters { All = false }, token)
                    .ConfigureAwait(false);
                // Pinned as `repo@sha256:…`, but the list may file that digest under another name
                // (`127.0.0.1:5000/…` pulled, `blocks-functions-node@…` listed) — so match the digest
                // alone, which with the containerd store is also the image id.
                var at = reference.IndexOf('@', StringComparison.Ordinal);
                var digest = at >= 0 ? reference[(at + 1)..] : null;
                var match = all.FirstOrDefault(i =>
                    (i.RepoTags?.Contains(reference, StringComparer.Ordinal) ?? false)
                    || string.Equals(i.ID, reference, StringComparison.Ordinal)
                    || (digest is not null
                        && (string.Equals(i.ID, digest, StringComparison.Ordinal)
                            || (i.RepoDigests?.Any(d => d.EndsWith("@" + digest, StringComparison.Ordinal)) ?? false))));
                return match is null ? 0 : Math.Max(match.Size, 0);
            }
            catch (DockerApiException ex)
            {
                _logger.LogDebug("Could not read the base image size: {Message}", ex.Message);
                return 0;
            }
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

        /// <summary>True for an image a test run built for itself.</summary>
        internal static bool IsTestImage(IDictionary<string, string>? labels) =>
            labels is not null
            && labels.TryGetValue(Builds.BuildProcessor.TestImageLabel, out var value)
            && string.Equals(value, "true", StringComparison.Ordinal);

        /// <summary>
        /// A test image past <see cref="TestImageMaxAge"/>: its run has long ended, so a runner died
        /// before deleting it. Pruned whatever the keep set says — no version points at a test image.
        /// </summary>
        internal static bool IsTestDepsImage(IDictionary<string, string>? labels) =>
            labels is not null
            && labels.TryGetValue(Builds.BuildProcessor.TestDepsImageLabel, out var value)
            && string.Equals(value, "true", StringComparison.Ordinal);

        internal static bool IsStaleTestImage(IDictionary<string, string>? labels, DateTime created, DateTime now) =>
            IsTestImage(labels) && created <= now - TestImageMaxAge;

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

        /// <summary>
        /// The repository part of an image reference — no tag, no digest.
        /// <para>
        /// A registry address carries a port, so the tag separator is only the last <c>:</c> when
        /// it comes after the last <c>/</c>. Splitting on the first colon instead turned
        /// <c>127.0.0.1:5000/blocks/functions-node:24-v1</c> into <c>127.0.0.1</c>, and since every
        /// tenant image on a host-local registry is also tagged <c>127.0.0.1:5000/…</c>, the
        /// base-image check below then matched all of them and Image GC pruned nothing at all.
        /// </para>
        /// </summary>
        internal static string RepositoryOf(string reference)
        {
            var at = reference.IndexOf('@', StringComparison.Ordinal);
            var withoutDigest = at > 0 ? reference[..at] : reference;

            var lastSlash = withoutDigest.LastIndexOf('/');
            var colon = withoutDigest.LastIndexOf(':');
            return colon > lastSlash ? withoutDigest[..colon] : withoutDigest;
        }

        private bool IsBaseImage(ImagesListResponse image)
            => IsOfRepository(image, _options.BaseImage)
            // The build image (RUNNER__BuildImage) is FROM-equivalent for the install sandbox:
            // pruning it would fail every build that has to install dependencies.
            || (!string.IsNullOrWhiteSpace(_options.BuildImage) && IsOfRepository(image, _options.BuildImage));

        private static bool IsOfRepository(ImagesListResponse image, string baseImage)
        {
            if (string.IsNullOrWhiteSpace(baseImage)) return false;

            // Compare on the repository, not the full reference: the base image is pinned by
            // digest in production and by tag locally, and both must survive. The comparison is
            // exact rather than a prefix — `…/functions-node` must not also match a tenant
            // repository that merely starts with the same characters.
            var baseRepo = RepositoryOf(baseImage);

            if (image.RepoTags is not null)
            {
                foreach (var tag in image.RepoTags)
                {
                    if (string.Equals(RepositoryOf(tag), baseRepo, StringComparison.Ordinal)) return true;
                }
            }
            if (image.RepoDigests is not null)
            {
                foreach (var digest in image.RepoDigests)
                {
                    if (string.Equals(RepositoryOf(digest), baseRepo, StringComparison.Ordinal)) return true;
                }
            }

            return false;
        }
    }
}
