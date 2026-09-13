using Blocks.Genesis;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    /// <summary>What a purge removed, for the log and the audit record.</summary>
    public sealed record FunctionPurgeReport(
        long Versions,
        long Builds,
        long Runs,
        long RunLogs,
        int ImagesReleased,
        int RunsCancelled)
    {
        public static readonly FunctionPurgeReport Empty = new(0, 0, 0, 0, 0, 0);
    }

    public interface IFunctionPurgeService
    {
        /// <summary>
        /// Removes everything a function leaves behind, other than its audit trail. Safe to call
        /// twice: every step is "remove what is there", so a purge that failed half way through
        /// finishes on the next attempt.
        /// </summary>
        Task<FunctionPurgeReport> PurgeAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Deleting a function used to delete the function document, its run stats and its versions,
    /// and leave the rest: build records, runs, run logs, the images those builds produced, and
    /// the keep-set entries that stopped any runner from ever reclaiming them. Deleted functions
    /// therefore kept costing disk on every runner for as long as the host lived.
    /// <para>
    /// The order here is deliberate. Everything that identifies work — image digests, in-flight
    /// runs, queued build sources — is read and dealt with <i>before</i> the records naming it are
    /// deleted, because after that there is nothing left to look them up with. The audit trail is
    /// the one thing kept: it is the only record that the function ever existed.
    /// </para>
    /// </summary>
    public class FunctionPurgeService : IFunctionPurgeService
    {
        private readonly IFunctionVersionRepository _versionRepository;
        private readonly IFunctionBuildRepository _buildRepository;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionRunLogRepository _runLogRepository;
        private readonly IFunctionRunStatsRepository _runStatsRepository;
        private readonly IFunctionImagePinService _imagePins;
        private readonly ICacheClient _cache;
        private readonly ILogger<FunctionPurgeService> _logger;

        /// <summary>Runs are cancelled a page at a time; a function with more in flight than this
        /// has bigger problems than a slow delete.</summary>
        private const int ActiveRunPageSize = 200;

        public FunctionPurgeService(
            IFunctionVersionRepository versionRepository,
            IFunctionBuildRepository buildRepository,
            IFunctionRunRepository runRepository,
            IFunctionRunLogRepository runLogRepository,
            IFunctionRunStatsRepository runStatsRepository,
            IFunctionImagePinService imagePins,
            ICacheClient cache,
            ILogger<FunctionPurgeService> logger)
        {
            _versionRepository = versionRepository;
            _buildRepository = buildRepository;
            _runRepository = runRepository;
            _runLogRepository = runLogRepository;
            _runStatsRepository = runStatsRepository;
            _imagePins = imagePins;
            _cache = cache;
            _logger = logger;
        }

        public async Task<FunctionPurgeReport> PurgeAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return FunctionPurgeReport.Empty;

            var versions = await _versionRepository.GetAllForFunctionAsync(tenantId, functionId, cancellationToken);
            var builds = await _buildRepository.GetAllForFunctionAsync(tenantId, functionId, cancellationToken);

            var cancelled = await CancelActiveRunsAsync(tenantId, functionId, cancellationToken);
            await DropQueuedBuildSourcesAsync(builds, cancellationToken);

            // Every image this function ever produced, from both sides: a version pins its digest,
            // and a build knows the digest of images that were only ever tested.
            var digests = versions.Select(v => v.ImageDigest)
                .Concat(builds.Select(b => b.ImageDigest))
                .ToList();
            var released = await _imagePins.ReleaseAsync(digests, stillReferenced: null, cancellationToken);

            var deletedVersions = await _versionRepository.DeleteAllForFunctionAsync(tenantId, functionId, cancellationToken);
            var deletedBuilds = await _buildRepository.DeleteAllForFunctionAsync(tenantId, functionId, cancellationToken);
            var deletedLogs = await _runLogRepository.DeleteAllForFunctionAsync(tenantId, functionId, cancellationToken);
            var deletedRuns = await _runRepository.DeleteAllForFunctionAsync(tenantId, functionId, cancellationToken);
            await _runStatsRepository.DeleteAsync(tenantId, functionId, cancellationToken);

            await DeleteRuntimeKeysAsync(functionId);

            var report = new FunctionPurgeReport(
                deletedVersions, deletedBuilds, deletedRuns, deletedLogs, released, cancelled);

            _logger.LogInformation(
                "Purged function {FunctionId}: {Versions} version(s), {Builds} build(s), {Runs} run(s), " +
                "{Logs} log line(s), {Images} image(s) unpinned, {Cancelled} run(s) cancelled",
                functionId, report.Versions, report.Builds, report.Runs, report.RunLogs,
                report.ImagesReleased, report.RunsCancelled);

            return report;
        }

        /// <summary>
        /// Signals every run that has not reached a terminal status. A sandbox that is mid-run
        /// would otherwise keep going and write its result against a function that is gone.
        /// </summary>
        private async Task<int> CancelActiveRunsAsync(
            string tenantId, string functionId, CancellationToken cancellationToken)
        {
            var cancelled = 0;
            try
            {
                var filter = new FunctionRunFilter(functionId, null, null, null, ActiveOnly: true);
                var (runs, _) = await _runRepository.GetAllAsync(
                    tenantId, filter, pageNumber: 0, pageSize: ActiveRunPageSize, cancellationToken);

                var database = _cache.CacheDatabase();
                foreach (var run in runs)
                {
                    await database.StringSetAsync(
                        FunctionQueueKeys.Cancel(run.ItemId), "1", FunctionQueueKeys.CancelTtl);
                    cancelled++;
                }
            }
            catch (Exception ex)
            {
                // A run that keeps going is wasted work, not corruption: its result lands on a run
                // record that no longer exists and is dropped. Not a reason to abandon the delete.
                _logger.LogWarning(ex, "Could not cancel in-flight runs of function {FunctionId}", functionId);
            }

            return cancelled;
        }

        /// <summary>
        /// Drops the source bundle behind any build that has not started. The runner reads the
        /// bundle when it claims the job and fails the build cleanly when it is gone, which is
        /// what should happen to a build for a function nobody has any more.
        /// </summary>
        private async Task DropQueuedBuildSourcesAsync(
            IReadOnlyList<Entities.FunctionBuildEntity> builds, CancellationToken cancellationToken)
        {
            try
            {
                var database = _cache.CacheDatabase();
                foreach (var build in builds.Where(b => b.Status is BuildStatus.Queued or BuildStatus.Building))
                {
                    await database.KeyDeleteAsync(FunctionQueueKeys.Source(build.ItemId));
                }
            }
            catch (Exception ex)
            {
                // These expire on their own after SourceTtl; missing them costs an hour of Redis.
                _logger.LogWarning(ex, "Could not drop queued build sources");
            }
        }

        /// <summary>
        /// The per-function Redis keys with no expiry of their own. Rate-limit counters are not
        /// here on purpose: they are minute- and day-bucketed and carry their own short TTLs, so
        /// they disappear without being chased.
        /// </summary>
        private async Task DeleteRuntimeKeysAsync(string functionId)
        {
            try
            {
                await _cache.CacheDatabase().KeyDeleteAsync(FunctionQueueKeys.Concurrency(functionId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete the concurrency key of function {FunctionId}", functionId);
            }
        }
    }
}
