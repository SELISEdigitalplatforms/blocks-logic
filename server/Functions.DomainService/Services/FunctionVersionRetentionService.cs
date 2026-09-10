using Blocks.Genesis;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Services
{
    public interface IFunctionVersionRetentionService
    {
        /// <summary>Prunes a function's versions down to the retention cap. Returns how many went.</summary>
        Task<int> PruneAsync(
            string tenantId, string functionId, string? activeVersionId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Keeps a function's version history bounded — by default the newest 10.
    /// <para>
    /// Versions are immutable and each one snapshots its own full copy of the source, so without
    /// a cap the only thing that ever grows is deploy history. Pruning is deliberately narrow:
    /// three things are always kept regardless of the cap.
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>The active version.</b> After a rollback the live version can be an old one, and
    /// deleting it would leave the function pointing at a record that no longer exists — every
    /// invocation would then fail to resolve a version.
    /// </item>
    /// <item>
    /// <b>Any version a non-terminal run still references.</b> A run that is queued, claimed,
    /// starting, running or processing outputs still reads its version for the output-action
    /// configuration and for a retry, and both degrade quietly if it has gone (the result
    /// consumer falls back to the function's current config; a retry is simply skipped).
    /// </item>
    /// <item>
    /// <b>Any image another surviving version shares.</b> Builds are content-addressed, so two
    /// versions with identical source have the same digest — releasing an image because one of
    /// them was pruned would pull it out from under the other.
    /// </item>
    /// </list>
    /// <para>
    /// Releasing the image is the second half of the job: the runner's <c>ImageGc</c> only prunes
    /// what is absent from <c>functions:images:keep</c>, and the runner's build processor only
    /// ever <i>adds</i> to that set — so until something removes entries, no function image is
    /// ever reclaimable. Pruning a version is exactly when its image stops being referenced.
    /// </para>
    /// </summary>
    public class FunctionVersionRetentionService : IFunctionVersionRetentionService
    {
        private const int DefaultMaxVersions = 10;

        private readonly IFunctionVersionRepository _versionRepository;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IDatabase _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FunctionVersionRetentionService> _logger;

        public FunctionVersionRetentionService(
            IFunctionVersionRepository versionRepository,
            IFunctionRunRepository runRepository,
            ICacheClient cache,
            IConfiguration configuration,
            ILogger<FunctionVersionRetentionService> logger)
        {
            _versionRepository = versionRepository;
            _runRepository = runRepository;
            _db = cache.CacheDatabase();
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<int> PruneAsync(
            string tenantId, string functionId, string? activeVersionId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var maxVersions = Math.Max(1, _configuration.GetValue("Functions:MaxVersionsPerFunction", DefaultMaxVersions));

                var versions = await _versionRepository.GetAllForFunctionAsync(tenantId, functionId, cancellationToken);
                if (versions.Count <= maxVersions) return 0;

                var inFlight = await _runRepository.GetVersionIdsWithActiveRunsAsync(tenantId, functionId, cancellationToken);

                // Newest-first from the repository, so the head of the list is the keep window.
                var keep = versions.Take(maxVersions).Select(v => v.ItemId).ToHashSet(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(activeVersionId)) keep.Add(activeVersionId);
                foreach (var versionId in inFlight) keep.Add(versionId);

                var doomed = versions.Where(v => !keep.Contains(v.ItemId)).ToList();
                if (doomed.Count == 0) return 0;

                var deleted = await _versionRepository.DeleteManyAsync(
                    tenantId, doomed.Select(v => v.ItemId).ToList(), cancellationToken);

                await ReleaseImagesAsync(versions, keep, doomed);

                _logger.LogInformation(
                    "Pruned {Deleted} version(s) of function {FunctionId} beyond the newest {MaxVersions}",
                    deleted, functionId, maxVersions);

                return (int)deleted;
            }
            catch (Exception ex)
            {
                // Retention is housekeeping: a deploy must not fail because history could not be
                // trimmed. The next deploy tries again.
                _logger.LogError(ex, "Pruning versions of function {FunctionId} failed", functionId);
                return 0;
            }
        }

        /// <summary>
        /// Removes a pruned version's image from the runner's keep set, unless a surviving
        /// version shares the same digest.
        /// </summary>
        private async Task ReleaseImagesAsync(
            IReadOnlyList<Entities.FunctionVersionEntity> all,
            IReadOnlySet<string> keptIds,
            IReadOnlyList<Entities.FunctionVersionEntity> doomed)
        {
            var stillReferenced = all
                .Where(v => keptIds.Contains(v.ItemId))
                .Select(v => v.ImageDigest)
                .Where(d => !string.IsNullOrEmpty(d))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var digest in doomed
                .Select(v => v.ImageDigest)
                .Where(d => !string.IsNullOrEmpty(d) && !stillReferenced.Contains(d))
                .Distinct(StringComparer.Ordinal))
            {
                await _db.SetRemoveAsync(FunctionQueueKeys.ImagesKeep, digest);
            }
        }
    }
}
