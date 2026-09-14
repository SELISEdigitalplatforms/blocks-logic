using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Common.InternalService.Access
{
    /// <summary>
    /// Default <see cref="IEndpointAccessAuthorizer"/>. Token validation mirrors what the Genesis bearer
    /// handler does for <c>[Authorize]</c> actions: the tenant's public certificate is read from the cache
    /// under <c>tetocertpublic::&lt;tenantId&gt;</c>, lifetime is enforced with zero clock skew, issuer /
    /// audience are not checked (the certificate is per tenant, so a token that verifies is that tenant's).
    /// </summary>
    public sealed class EndpointAccessAuthorizer : IEndpointAccessAuthorizer
    {
        private const string PublicCertCachePrefix = "tetocertpublic::";

        // The same keys Genesis' (internal) TenantContextHelper accepts, in the same order.
        private static readonly string[] TenantResolutionKeys = ["tenant_id", BlocksConstants.BlocksKey];

        private readonly ITenants _tenants;
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<EndpointAccessAuthorizer> _logger;

        public EndpointAccessAuthorizer(ITenants tenants, ICacheClient cacheClient, ILogger<EndpointAccessAuthorizer> logger)
        {
            _tenants = tenants;
            _cacheClient = cacheClient;
            _logger = logger;
        }

        public Task<string?> ResolveTenantIdAsync(HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            foreach (var key in TenantResolutionKeys)
            {
                if (request.Headers.TryGetValue(key, out var header) && !string.IsNullOrWhiteSpace(header.ToString()))
                {
                    return Task.FromResult<string?>(header.ToString().Trim());
                }
            }

            foreach (var key in TenantResolutionKeys)
            {
                if (request.Query.TryGetValue(key, out var query) && !string.IsNullOrWhiteSpace(query.ToString()))
                {
                    return Task.FromResult<string?>(query.ToString().Trim());
                }
            }

            // Last resort: the tenant claim of a presented token, read WITHOUT verification. It only selects
            // the tenant whose certificate then has to validate that very token, so a forged claim buys nothing.
            try
            {
                var token = TokenHelper.GetToken(request, _tenants).Token;
                if (string.IsNullOrWhiteSpace(token))
                {
                    return Task.FromResult<string?>(null);
                }

                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                var claim = jwt.Claims.FirstOrDefault(c => c.Type == BlocksContext.TENANT_ID_CLAIM)?.Value;
                return Task.FromResult(string.IsNullOrWhiteSpace(claim) ? null : claim.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Endpoint access: could not read a tenant from the bearer token.");
                return Task.FromResult<string?>(null);
            }
        }

        public async Task<EndpointAccessDecision> AuthorizeAsync(
            HttpRequest request, string tenantId, EndpointAccessPolicy policy, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(policy);

            if (policy.IsPublic)
            {
                return EndpointAccessDecision.Public();
            }

            // Strict ordering: rules never run for an unauthenticated caller.
            var (principal, rawToken) = await ValidateTokenAsync(request, tenantId).ConfigureAwait(false);
            if (principal is null)
            {
                return EndpointAccessDecision.Unauthenticated("A valid Blocks token is required.");
            }

            if (!EndpointAccessEvaluator.Evaluate(principal, policy))
            {
                _logger.LogWarning(
                    "Endpoint access: caller {UserId} of tenant {TenantId} fails the configured role / permission rules.",
                    EndpointAccessEvaluator.GetUserId(principal), tenantId);
                return EndpointAccessDecision.Forbidden("The caller does not hold the required roles or permissions.");
            }

            var context = BuildContext(request, tenantId, principal, rawToken);
            if (context is null)
            {
                return EndpointAccessDecision.Unauthenticated("Unknown tenant.");
            }

            AttachUser(request, principal);
            return EndpointAccessDecision.Allowed(context, principal, rawToken);
        }

        public async Task<(ClaimsPrincipal? Principal, string? RawToken)> ValidateTokenAsync(HttpRequest request, string tenantId)
        {
            ArgumentNullException.ThrowIfNull(request);

            var tenant = string.IsNullOrWhiteSpace(tenantId) ? null : _tenants.GetTenantByID(tenantId);
            if (tenant is null)
            {
                return (null, null);
            }

            var (token, _) = TokenHelper.GetToken(request, _tenants);
            if (string.IsNullOrEmpty(token))
            {
                return (null, null);
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
                if (!tokenHandler.CanReadToken(token))
                {
                    return (null, null);
                }

                // Impersonated tokens are signed by the ORIGINAL (root) tenant, not the tenant being acted in,
                // so the certificate is picked from the token's own (still unverified) claims and the binding
                // to the requested tenant is checked after the signature holds.
                var jwt = tokenHandler.ReadJwtToken(token);
                var signingTenant = ResolveSigningTenant(jwt, tenant);
                if (signingTenant is null)
                {
                    return (null, null);
                }

                var cacheKey = $"{PublicCertCachePrefix}{signingTenant.TenantId}";
                var certificateData = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey).ConfigureAwait(false);
                var validationParams = signingTenant.JwtTokenParameters;
                var publicCert = X509CertificateLoader.LoadPkcs12(certificateData, validationParams.PublicCertificatePassword);
                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    IssuerSigningKey = new X509SecurityKey(publicCert),
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    SaveSigninToken = true,
                };
                var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out _);
                if (!ImpersonatedTokenMatchesTenant(principal, tenantId, signingTenant.TenantId))
                {
                    _logger.LogWarning(
                        "Endpoint access: impersonated token is not bound to tenant {TenantId} / signing tenant {SigningTenantId}.",
                        tenantId, signingTenant.TenantId);
                    return (null, null);
                }

                return (principal, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Endpoint access: bearer token validation failed. TenantId={TenantId}", tenantId);
                return (null, null);
            }
        }

        /// <summary>
        /// Picks the tenant whose public certificate must verify <paramref name="jwt"/>. The unverified claims
        /// are a key-selection hint only; the signature is checked afterwards. An impersonated token with a
        /// missing or unknown <c>original_tenant_id</c> fails closed.
        /// </summary>
        private Tenant? ResolveSigningTenant(JwtSecurityToken jwt, Tenant requestedTenant)
        {
            var impersonated = jwt.Claims.FirstOrDefault(c => c.Type == BlocksContext.IMPERSONATED_CLAIM)?.Value == "true";
            if (!impersonated)
            {
                return requestedTenant;
            }

            var originalTenantId = jwt.Claims.FirstOrDefault(c => c.Type == BlocksContext.ORIGINAL_TENANT_ID_CLAIM)?.Value;
            return string.IsNullOrWhiteSpace(originalTenantId) ? null : _tenants.GetTenantByID(originalTenantId);
        }

        /// <summary>
        /// After signature validation an impersonated token must still target the requested tenant and name
        /// the signing tenant as <c>original_tenant_id</c>; otherwise a root-tenant token minted for tenant A
        /// could be replayed against tenant B. Non-impersonated tokens skip this: their certificate already
        /// ties them to the requested tenant.
        /// </summary>
        private static bool ImpersonatedTokenMatchesTenant(ClaimsPrincipal principal, string requestedTenantId, string signingTenantId)
        {
            if (!EndpointAccessEvaluator.GetImpersonated(principal))
            {
                return true;
            }

            return string.Equals(EndpointAccessEvaluator.GetClaimValue(principal, BlocksContext.TENANT_ID_CLAIM), requestedTenantId, StringComparison.Ordinal)
                && string.Equals(EndpointAccessEvaluator.GetClaimValue(principal, BlocksContext.ORIGINAL_TENANT_ID_CLAIM), signingTenantId, StringComparison.Ordinal);
        }

        public BlocksContext? BuildContext(HttpRequest request, string tenantId, ClaimsPrincipal principal, string? rawToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(principal);

            var tenant = _tenants.GetTenantByID(tenantId);
            if (tenant is null)
            {
                return null;
            }

            var applicationDomain = tenant.Applications?.FirstOrDefault()?.Domain ?? string.Empty;

            // An impersonation token (a cloud / root-tenant user acting inside this tenant) carries the root
            // tenant in original_tenant_id; keep it so audit and permission lookups can tell the two apart.
            // Non-impersonated callers have no such claim and the original tenant is simply this tenant.
            var impersonated = EndpointAccessEvaluator.GetImpersonated(principal);
            var originalTenantClaim = impersonated
                ? EndpointAccessEvaluator.GetClaimValue(principal, BlocksContext.ORIGINAL_TENANT_ID_CLAIM)
                : string.Empty;
            var originalTenantId = string.IsNullOrWhiteSpace(originalTenantClaim) ? tenant.TenantId : originalTenantClaim;

            return BlocksContext.Create(
                tenantId: tenant.TenantId,
                roles: EndpointAccessEvaluator.GetRoles(principal),
                userId: EndpointAccessEvaluator.GetUserId(principal),
                isAuthenticated: true,
                requestUri: request.Path.Value ?? string.Empty,
                organizationId: EndpointAccessEvaluator.GetOrganization(principal),
                expireOn: EndpointAccessEvaluator.GetExpireOn(principal),
                email: EndpointAccessEvaluator.GetClaimValue(principal, BlocksContext.EMAIL_CLAIM),
                permissions: EndpointAccessEvaluator.GetPermissions(principal),
                userName: EndpointAccessEvaluator.GetClaimValue(principal, BlocksContext.USER_NAME_CLAIM),
                phoneNumber: EndpointAccessEvaluator.GetClaimValue(principal, BlocksContext.PHONE_NUMBER_CLAIM),
                displayName: EndpointAccessEvaluator.GetClaimValue(principal, BlocksContext.DISPLAY_NAME_CLAIM),
                oauthToken: rawToken ?? string.Empty,
                originalTenantId: originalTenantId,
                applicationDomain: applicationDomain,
                impersonated: impersonated,
                impersonationSessionId: EndpointAccessEvaluator.GetClaimValue(principal, BlocksContext.IMPERSONATION_SESSION_ID_CLAIM));
        }

        /// <summary>
        /// Puts the validated caller on the request so ambient <see cref="BlocksContext.GetContext"/> and the
        /// delegation-grant factory see an authenticated user (the JWT middleware never ran on this endpoint).
        /// </summary>
        private static void AttachUser(HttpRequest request, ClaimsPrincipal principal)
        {
            if (request.HttpContext is not null)
            {
                request.HttpContext.User = principal;
            }
        }
    }
}
