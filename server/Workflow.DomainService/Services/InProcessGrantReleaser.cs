using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// Ends the delegation grant an in-process run minted for itself (webhook <c>"last"</c> mode and the
    /// editor's Execute step) once that run has returned.
    /// </summary>
    public interface IInProcessGrantReleaser
    {
        /// <summary>Deletes the grant and drops its cached token. Never throws.</summary>
        Task ReleaseAsync(string? grantId);
    }

    /// <summary>
    /// A queued hop's grant is deleted by the Genesis worker after the message settles; an in-process run
    /// sends no message, so its grant used to stay redeemable for the store's full default lifetime (two days).
    /// Releasing it once the run returns is safe: every send the run made chained its own grant at send time,
    /// and a function step creates its own grant at invoke time, so nothing still points at this one.
    /// <para>The grant id is a bearer-like credential: it is never logged.</para>
    /// </summary>
    public sealed class InProcessGrantReleaser : IInProcessGrantReleaser
    {
        private readonly IDelegationGrantStore _grants;
        private readonly IDelegatedTokenProvider _tokens;
        private readonly ILogger<InProcessGrantReleaser> _logger;

        public InProcessGrantReleaser(
            IDelegationGrantStore grants, IDelegatedTokenProvider tokens, ILogger<InProcessGrantReleaser> logger)
        {
            _grants = grants;
            _tokens = tokens;
            _logger = logger;
        }

        public async Task ReleaseAsync(string? grantId)
        {
            if (string.IsNullOrWhiteSpace(grantId)) return;
            try
            {
                _tokens.Invalidate(grantId);
                await _grants.DeleteAsync(grantId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The store's absolute TTL still ends the grant.
                _logger.LogWarning("Could not release an in-process delegation grant ({ExceptionType})", ex.GetType().Name);
            }
        }
    }
}
