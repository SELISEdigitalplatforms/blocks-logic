using System.Security.Claims;
using Blocks.Genesis;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    public interface IFunctionDelegationService
    {
        /// <summary>
        /// Writes a Genesis delegation grant for the run's caller and returns its id, or null when
        /// this run gets no <c>ctx.blocks.accessToken</c>. Never throws: a run must not fail, or
        /// fail to queue, because delegation could not be set up.
        /// </summary>
        Task<string?> CreateGrantAsync(string tenantId, BlocksContext? caller, AuthMode authMode);

        /// <summary>Best-effort removal of a grant whose run never got queued.</summary>
        Task DeleteGrantAsync(string? grantId);
    }

    /// <summary>
    /// The control plane's half of <c>ctx.blocks.accessToken</c>: lets a function call Blocks APIs
    /// as the caller who invoked it, without a token ever entering the queue or the envelope.
    /// <para>
    /// At invoke time, while the caller's validated token is still in scope, this writes a Genesis
    /// delegation grant — a Redis record naming the tenant, user, organization and the user's
    /// <c>token_version</c>/<c>security_stamp</c>, with an absolute TTL — and the run carries only
    /// the grant's opaque id, beside the envelope. The runner redeems it with IAM's RFC 8693
    /// token exchange right before the sandbox starts; IAM mints a fresh, short-lived token and
    /// refuses if the user was deactivated or their sessions revoked since. This is the same
    /// mechanism every Blocks message worker uses, and the workflow engine before it.
    /// </para>
    /// <para>
    /// <b>Who gets one.</b> Any authenticated user on a non-public trigger, calling within the
    /// run's own tenant. A <c>client_credentials</c> caller gets none: such a caller already holds
    /// a credential it can give the function as a secret. A public trigger, a schedule, or
    /// anything else without a user: none.
    /// </para>
    /// <para>
    /// <b>Impersonation is included</b>, and is the ordinary case here: the Functions pages sit
    /// under the console's impersonate route, so clicking Test is almost always an impersonated
    /// caller. Refusing those was why a developer's own function could not call Blocks at all.
    /// What makes it safe is not refusing here but what the grant carries: Genesis records the
    /// impersonation session on it, and IAM resolves the user in the tenant they really live in,
    /// refuses once the session stops, and mints a token that still says it is impersonated. The
    /// function ends up with exactly the authority of the console session that started it —
    /// neither a plain unflagged token nor nothing at all.
    /// </para>
    /// <para>
    /// <b>Version material</b> comes from the validated token's claims on the current request
    /// (HTTP invoke, Test, Replay), or — in a worker with no request, as a workflow step — from the
    /// grant that flow is already holding, and only when it names the same tenant, user and
    /// organization. Without either there is no grant: IAM compares both values on redemption.
    /// </para>
    /// </summary>
    public sealed class FunctionDelegationService : IFunctionDelegationService
    {
        private readonly IDelegationGrantStore _grants;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<FunctionDelegationService> _logger;

        public FunctionDelegationService(
            IDelegationGrantStore grants,
            IHttpContextAccessor httpContextAccessor,
            ILogger<FunctionDelegationService> logger)
        {
            _grants = grants;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<string?> CreateGrantAsync(string tenantId, BlocksContext? caller, AuthMode authMode)
        {
            // Every arm says why. The cost of this being silent was a day of looking in the wrong
            // place: the run simply logged blocks-skipped, and nothing anywhere said a grant had
            // been declined, let alone which condition declined it.
            if (authMode == AuthMode.Public)
            {
                _logger.LogDebug(
                    "A public run in tenant {TenantId} gets no access token; that is what public means",
                    tenantId);
                return null;
            }

            if (caller is null || !caller.IsAuthenticated || string.IsNullOrWhiteSpace(caller.UserId))
            {
                _logger.LogDebug(
                    "No authenticated user behind a run in tenant {TenantId}; it gets no access token",
                    tenantId);
                return null;
            }

            if (string.IsNullOrWhiteSpace(tenantId)
                || !string.Equals(caller.TenantId, tenantId, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "The caller of a run in tenant {TenantId} belongs to a different tenant; it gets no access token",
                    tenantId);
                return null;
            }

            try
            {
                var (tokenVersion, securityStamp) = ReadFromRequest(caller);
                if (tokenVersion is null || securityStamp is null)
                {
                    (tokenVersion, securityStamp) = await ReadFromHeldGrantAsync(caller).ConfigureAwait(false);
                }

                if (tokenVersion is null || securityStamp is null)
                {
                    // A Warning, not Debug: an authenticated user reached this point, so a missing
                    // claim is a token problem (IAM writes security_stamp "" for a user without one),
                    // not the expected "nobody to delegate" case above.
                    _logger.LogWarning(
                        "No token_version/security_stamp for the caller of a run in tenant {TenantId} (token_version {HasTokenVersion}, security_stamp {HasSecurityStamp}); it gets no access token",
                        tenantId, tokenVersion is not null, securityStamp is not null);
                    return null;
                }

                return await _grants.CreateAsync(caller, tokenVersion, securityStamp, FunctionQueueKeys.DelegationGrantTtl)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The type only: the grant id is a bearer credential and nothing here vouches for
                // what an arbitrary exception's message holds.
                _logger.LogWarning(
                    "Could not create a delegation grant for a run in tenant {TenantId} ({ExceptionType}); it gets no access token",
                    tenantId, ex.GetType().Name);
                return null;
            }
        }

        public async Task DeleteGrantAsync(string? grantId)
        {
            if (string.IsNullOrWhiteSpace(grantId)) return;
            try
            {
                await _grants.DeleteAsync(grantId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The store already swallows its own failures; this is for one that does not. The
                // absolute TTL removes the grant either way.
                _logger.LogWarning("Could not delete an unused delegation grant ({ExceptionType})", ex.GetType().Name);
            }
        }

        /// <summary>
        /// The two values from the validated token on this request — and only when that token is
        /// the caller's own, so a request made under one identity can never stamp a grant for
        /// another.
        /// </summary>
        private (string? TokenVersion, string? SecurityStamp) ReadFromRequest(BlocksContext caller)
        {
            if (_httpContextAccessor.HttpContext?.User?.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            {
                return (null, null);
            }

            if (!string.Equals(identity.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value, caller.UserId, StringComparison.Ordinal))
            {
                return (null, null);
            }

            return (
                NonBlank(identity.FindFirst(DelegationGrantFactory.TokenVersionClaim)?.Value),
                NonBlank(identity.FindFirst(DelegationGrantFactory.SecurityStampClaim)?.Value));
        }

        /// <summary>
        /// A worker flow (a workflow step) carries the grant it was started with. Its values are
        /// carried forward only when that grant names exactly this caller.
        /// </summary>
        private async Task<(string? TokenVersion, string? SecurityStamp)> ReadFromHeldGrantAsync(BlocksContext caller)
        {
            var held = DelegatedTokenContext.Current;
            if (string.IsNullOrWhiteSpace(held)) return (null, null);

            var record = await _grants.GetAsync(held).ConfigureAwait(false);
            // Matching the user also rules out a client grant, which names no user.
            if (record is null
                || !string.Equals(record.TenantId, caller.TenantId, StringComparison.Ordinal)
                || !string.Equals(record.UserId, caller.UserId, StringComparison.Ordinal)
                || !string.Equals(record.OrganizationId ?? string.Empty, caller.OrganizationId ?? string.Empty, StringComparison.Ordinal))
            {
                return (null, null);
            }

            return (NonBlank(record.TokenVersion), NonBlank(record.SecurityStamp));
        }

        private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
