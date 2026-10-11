using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Repositories;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    public interface IFunctionImageRecoveryService
    {
        /// <summary>
        /// Reacts to a finished run. Where the run failed because its image could not be pulled,
        /// the build that produced that image is marked failed so the cache stops handing it out.
        /// Returns how many build records were invalidated — zero for every other outcome.
        /// </summary>
        Task<long> HandleRunOutcomeAsync(
            string tenantId, FunctionRunEntity run, RunErrorCode? errorCode,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The recovery path for an image that is no longer there.
    /// <para>
    /// A succeeded build is permanent and is what every later Test and Deploy of the same source
    /// reuses (DECISIONS D3). That is right while its image exists — and a trap once it does not:
    /// each attempt hands out the same missing digest, fails to pull it, and leaves nothing in the
    /// product able to break the loop, because only a source change or an explicit rebuild
    /// displaces a cached success. Invalidating the build turns the next attempt into a build.
    /// </para>
    /// </summary>
    public class FunctionImageRecoveryService : IFunctionImageRecoveryService
    {
        internal const string InvalidationReason =
            "the image this build produced could not be pulled; it will be built again";

        private readonly IFunctionBuildRepository _buildRepository;
        private readonly ILogger<FunctionImageRecoveryService> _logger;

        public FunctionImageRecoveryService(
            IFunctionBuildRepository buildRepository,
            ILogger<FunctionImageRecoveryService> logger)
        {
            _buildRepository = buildRepository;
            _logger = logger;
        }

        public async Task<long> HandleRunOutcomeAsync(
            string tenantId, FunctionRunEntity run, RunErrorCode? errorCode,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(run);

            if (errorCode != RunErrorCode.ImagePullFailed) return 0;

            if (string.IsNullOrEmpty(run.ImageDigest))
            {
                // A run recorded before the digest was kept on the run: there is nothing here that
                // names the image, and guessing from the function's current source could invalidate
                // a build that is perfectly good.
                _logger.LogWarning(
                    "Run {RunId} could not pull its image but does not record which one", run.ItemId);
                return 0;
            }

            try
            {
                var invalidated = await _buildRepository.InvalidateByImageDigestAsync(
                    tenantId, run.ImageDigest, InvalidationReason, cancellationToken);

                if (invalidated > 0)
                {
                    _logger.LogWarning(
                        "Image {Digest} could not be pulled for run {RunId}; invalidated {Count} build(s) so the " +
                        "next Test or Deploy builds again", run.ImageDigest, run.ItemId, invalidated);
                }

                return invalidated;
            }
            catch (Exception ex)
            {
                // The run is already recorded as failed. This is recovery for the next attempt,
                // and a result must still be applied even when it cannot be done.
                _logger.LogError(ex, "Could not invalidate builds for image {Digest}", run.ImageDigest);
                return 0;
            }
        }
    }
}
