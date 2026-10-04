using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace Blocks.FunctionRunner.Delegation
{
    /// <summary>
    /// Redeems a run's grant through Genesis's <see cref="IDelegatedTokenProvider"/> — the same
    /// RFC 8693 exchange every Blocks message worker uses: signed with the tenant's salt (read
    /// through <see cref="ITenants"/>, so the grant id alone is useless to anyone who lifts it from
    /// Redis), sent to IAM's token endpoint (resolved from <c>BLOCKS_IAM_BASE_URL</c>, which
    /// Genesis already requires of this host at startup). IAM re-reads the user on every
    /// redemption and refuses a deactivated user or one whose sessions were revoked since the
    /// grant was written.
    /// <para>
    /// The provider reads the grant and the tenant from ambient context, and the runner has no
    /// request and so no ambient context. Both are set inside an async method, so they cannot leak
    /// into the runner's other work — an AsyncLocal assignment does not flow back to the caller —
    /// exactly as <see cref="SecretStore.BlocksSecretsRunResolver"/> does for secrets.
    /// </para>
    /// <para>
    /// Every attempt redeems afresh: the provider's per-grant cache is dropped after each call, so
    /// a retry an hour later is re-checked by IAM rather than handed a token cached before the
    /// user was deactivated.
    /// </para>
    /// </summary>
    public sealed class DelegatedRunAccessTokenResolver : IRunAccessTokenResolver
    {
        /// <summary>
        /// How long a redemption may take before the run starts without a token. The provider's
        /// HTTP client has no timeout of its own worth waiting for, and a slow IAM must not hold a
        /// sandbox slot.
        /// </summary>
        internal TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

        private readonly IDelegatedTokenProvider _provider;
        private readonly ITenants _tenants;
        private readonly ILogger<DelegatedRunAccessTokenResolver> _logger;

        public DelegatedRunAccessTokenResolver(
            IDelegatedTokenProvider provider, ITenants tenants, ILogger<DelegatedRunAccessTokenResolver> logger)
        {
            _provider = provider;
            _tenants = tenants;
            _logger = logger;
        }

        public async Task<string?> RedeemAsync(string tenantId, string grantId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !DelegationGrantStore.IsWellFormed(grantId))
            {
                return null;
            }

            Tenant? tenant;
            try
            {
                tenant = _tenants.GetTenantByID(tenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Could not look up tenant {TenantId} to redeem a delegation grant ({ExceptionType})",
                    tenantId, ex.GetType().Name);
                return null;
            }

            if (tenant is null)
            {
                _logger.LogWarning("Tenant {TenantId} is unknown; its delegation grant was not redeemed", tenantId);
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            try
            {
                return await RedeemInContextAsync(tenant, grantId, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "IAM did not redeem a delegation grant for tenant {TenantId} within {Seconds}s",
                    tenantId, Timeout.TotalSeconds);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The type is enough to tell a DNS failure from a refused connection; the message is
                // left out because nothing here vouches for what an arbitrary exception's text holds.
                _logger.LogWarning(
                    "A delegation grant for tenant {TenantId} could not be redeemed ({ExceptionType})",
                    tenantId, ex.GetType().Name);
                return null;
            }
            finally
            {
                _provider.Invalidate(grantId);
            }
        }

        private async Task<string?> RedeemInContextAsync(Tenant tenant, string grantId, CancellationToken token)
        {
            // Tenant only. The provider needs nothing else from the context, and the identity the
            // token carries comes from the grant record in IAM, never from here.
            var applicationDomain = tenant.Applications?.FirstOrDefault()?.Domain ?? string.Empty;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenant.TenantId,
                roles: [],
                userId: string.Empty,
                isAuthenticated: false,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddMinutes(5),
                email: string.Empty,
                permissions: [],
                userName: string.Empty,
                phoneNumber: string.Empty,
                displayName: string.Empty,
                oauthToken: string.Empty,
                originalTenantId: tenant.TenantId,
                applicationDomain: applicationDomain,
                impersonated: false,
                impersonationSessionId: string.Empty), changeContext: true);
            DelegatedTokenContext.Set(grantId);

            try
            {
                var accessToken = await _provider.GetTokenAsync(token).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
            }
            finally
            {
                DelegatedTokenContext.Clear();
            }
        }
    }
}
