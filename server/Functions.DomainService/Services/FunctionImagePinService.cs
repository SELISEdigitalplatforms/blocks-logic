using Blocks.Genesis;
using Functions.DomainService.Queue;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Decides which images a runner must keep. The runner's Image GC prunes any function image
    /// that no container uses, is past its grace window, and is not in this set.
    /// </summary>
    public interface IFunctionImagePinService
    {
        /// <summary>Pins one image so no runner reclaims it. Null or empty digests are ignored.</summary>
        Task PinAsync(string? imageDigest, CancellationToken cancellationToken = default);

        /// <summary>
        /// Unpins images. Digests in <paramref name="stillReferenced"/> are left pinned, so
        /// releasing one version's image cannot reclaim an image another version shares.
        /// </summary>
        Task<int> ReleaseAsync(
            IEnumerable<string?> imageDigests,
            IReadOnlySet<string>? stillReferenced = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One place that writes <c>functions:images:keep</c>.
    /// <para>
    /// A build used to pin its own image, which meant every Test of changed source pinned one for
    /// good: nothing removed it when the build record later expired, and the digest outlived every
    /// record of what it belonged to. Pinning now tracks deployed versions only — the one thing
    /// that must survive on disk — and everything else relies on the GC's grace window, with a
    /// missing image being a rebuild rather than a failure.
    /// </para>
    /// </summary>
    public class FunctionImagePinService : IFunctionImagePinService
    {
        private readonly ICacheClient _cache;
        private readonly ILogger<FunctionImagePinService> _logger;

        public FunctionImagePinService(ICacheClient cache, ILogger<FunctionImagePinService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task PinAsync(string? imageDigest, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imageDigest)) return;

            try
            {
                await _cache.CacheDatabase().SetAddAsync(FunctionQueueKeys.ImagesKeep, imageDigest);
            }
            catch (Exception ex)
            {
                // A deploy that has written its version must not fail because the keep set could
                // not be updated; the worst case is an image reclaimed early and rebuilt.
                _logger.LogError(ex, "Could not pin image {Digest}", imageDigest);
            }
        }

        public async Task<int> ReleaseAsync(
            IEnumerable<string?> imageDigests,
            IReadOnlySet<string>? stillReferenced = null,
            CancellationToken cancellationToken = default)
        {
            var released = 0;
            var database = _cache.CacheDatabase();

            foreach (var digest in imageDigests
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d!)
                .Distinct(StringComparer.Ordinal))
            {
                if (stillReferenced is not null && stillReferenced.Contains(digest)) continue;

                try
                {
                    if (await database.SetRemoveAsync(FunctionQueueKeys.ImagesKeep, digest)) released++;
                }
                catch (Exception ex)
                {
                    // Leaving a digest pinned wastes disk on every runner, so it is worth an error
                    // rather than a debug line — but it must not abort the rest of a cleanup.
                    _logger.LogError(ex, "Could not release image {Digest} from the keep set", digest);
                }
            }

            return released;
        }
    }
}
