using Blocks.Genesis;
using Functions.DomainService.Queue;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Ends a run's delegation grant (<see cref="FunctionQueueKeys.RunDelegationField"/>) once the run is final.
    /// </summary>
    public interface IFunctionRunGrantRevoker
    {
        /// <summary>
        /// Deletes the grant named on the run's Redis hash and clears that field. Call it only when no
        /// further attempt of the run can start: every attempt redeems the same grant. Never throws.
        /// </summary>
        Task RevokeAsync(string? runId);
    }

    /// <summary>
    /// Before this, a finished run kept its grant id on the run hash for the hash's 6 h TTL and the grant
    /// itself lived its full 2 h (<see cref="FunctionQueueKeys.DelegationGrantTtl"/>), redeemable for a fresh
    /// caller token the whole time. Now the grant is deleted, and the field cleared, only where the run is
    /// provably finished on the runner and no attempt will start again:
    /// <list type="bullet">
    /// <item>the Worker applied the runner's result and no retry is scheduled (success, failure with no retry
    /// left, timeout, cancel, resource exceeded). The runner writes the results-stream entry only after the
    /// whole call ends — <c>ctx.waitUntil</c> work and any on-demand token redemption included — and output
    /// actions run on the host without the grant;</item>
    /// <item>a scheduled retry that is given up (run gone, version gone, no image, envelope expired or
    /// unpatchable, re-enqueue failed).</item>
    /// </list>
    /// Not here, on purpose: dead-letter, stale-run sweep and purge, where a runner may still be executing the
    /// attempt and could still redeem the grant, and a retry dropped after too many claims (proving the run is
    /// still on its last attempt would need another read on that failure path). Those keep ending by the
    /// grant's own TTL, as before.
    /// <para>The grant id is a bearer-like credential: it is never logged.</para>
    /// </summary>
    public sealed class FunctionRunGrantRevoker : IFunctionRunGrantRevoker
    {
        private readonly ICacheClient _cache;
        private readonly IFunctionDelegationService _delegation;
        private readonly ILogger<FunctionRunGrantRevoker> _logger;

        public FunctionRunGrantRevoker(
            ICacheClient cache, IFunctionDelegationService delegation, ILogger<FunctionRunGrantRevoker> logger)
        {
            _cache = cache;
            _delegation = delegation;
            _logger = logger;
        }

        public async Task RevokeAsync(string? runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) return;
            try
            {
                var db = _cache.CacheDatabase();
                var runKey = FunctionQueueKeys.Run(runId);
                var grantId = await db.HashGetAsync(runKey, FunctionQueueKeys.RunDelegationField).ConfigureAwait(false);
                if (grantId.IsNullOrEmpty) return;

                await _delegation.DeleteGrantAsync(grantId.ToString()).ConfigureAwait(false);
                await db.HashDeleteAsync(runKey, FunctionQueueKeys.RunDelegationField).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not revoke the delegation grant of run {RunId} ({ExceptionType}); its TTL still ends it",
                    runId, ex.GetType().Name);
            }
        }
    }
}
