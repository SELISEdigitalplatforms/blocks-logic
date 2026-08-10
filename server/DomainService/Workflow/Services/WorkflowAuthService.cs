using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace DomainService.Workflow.Services
{
    public class WorkflowAuthService : IWorkflowAuthService
    {
        private readonly ITenants _tenants;
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<WorkflowAuthService> _logger;
        private const string PublicCertCachePrefix = "tetocertpublic::";

        public WorkflowAuthService(ITenants tenants, ICacheClient cacheClient, ILogger<WorkflowAuthService> logger)
        {
            _tenants = tenants;
            _cacheClient = cacheClient;
            _logger = logger;
        }

        public static string GetAudience(Tenant? tenant)
        {
            var configuredAudience = tenant?.JwtTokenParameters?.Audiences?
                .FirstOrDefault(audience => !string.IsNullOrWhiteSpace(audience));

            if (!string.IsNullOrWhiteSpace(configuredAudience))
            {
                return configuredAudience.Trim();
            }

            return "api://blocks-protected-api";
        }

        public async Task<bool> IsAuthenticated(HttpRequest request, string tenantId)
        {
            var principal = await ValidateTokenAsync(request, tenantId);
            if (principal is null)
            {
                _logger.LogWarning("Workflow webhook authentication failed. TenantId={TenantId}", tenantId);
                return false;
            }
            return true;
        }

        public async Task<bool> IsAuthorized(HttpRequest request, string tenantId, AuthorizationConfig config)
        {
            // Step 1 — authenticate first. Strict ordering: RBAC never runs for an unauthenticated caller.
            var principal = await ValidateTokenAsync(request, tenantId);
            if (principal is null || string.IsNullOrWhiteSpace(GetUserId(principal)))
            {
                _logger.LogWarning("Workflow webhook authorization failed (unauthenticated). TenantId={TenantId}", tenantId);
                return false;
            }

            // Step 2 — authorize org + roles + permissions combined by matchType.
            var failureReason = EvaluateAuthorization(principal, config);
            if (failureReason is not null)
            {
                _logger.LogWarning("Workflow webhook authorization failed. TenantId={TenantId}, UserId={UserId}, Reason={Reason}",
                    tenantId, GetUserId(principal), failureReason);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates the bearer token of a webhook request against the tenant's public cert
        /// and returns the resulting <see cref="ClaimsPrincipal"/>. Returns <c>null</c> on any
        /// failure (missing token, unknown tenant, invalid signature, expired, etc.).
        /// Validation runs exactly once per request; both IsAuthenticated and IsAuthorized reuse this.
        /// </summary>
        private async Task<ClaimsPrincipal?> ValidateTokenAsync(HttpRequest request, string tenantId)
        {
            var tenant = _tenants.GetTenantByID(tenantId);
            if (tenant == null) return null;

            var (token, _) = TokenHelper.GetToken(request, _tenants);
            if (string.IsNullOrEmpty(token)) return null;

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                string cacheKey = $"{PublicCertCachePrefix}{tenant.TenantId}";
                var certificateData = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
                var validationParams = tenant.JwtTokenParameters;
                var publicCert = X509CertificateLoader.LoadPkcs12(certificateData, validationParams.PublicCertificatePassword);
                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    IssuerSigningKey = new X509SecurityKey(publicCert),
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    SaveSigninToken = true
                };
                return tokenHandler.ValidateToken(token, tokenValidationParameters, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow webhook token validation threw. TenantId={TenantId}", tenantId);
                return null;
            }
        }

        /// <summary>
        /// Pure evaluation of org + roles + permissions rules against the validated caller.
        /// Returns <c>null</c> on success, or a short reason string on failure ("org" | "roles" | "permissions" | "match").
        /// Public + static so it can be unit-tested without issuing a real signed JWT.
        /// </summary>
        public static string? EvaluateAuthorization(ClaimsPrincipal principal, AuthorizationConfig config)
        {
            var callerOrg = GetOrganization(principal);
            var callerRoles = GetRoles(principal);
            var callerPermissions = GetPermissions(principal);

            // Organization: "default" sentinel = caller's own org (from JWT) is always accepted.
            if (!string.Equals(config.OrganizationId, "default", StringComparison.Ordinal)
                && !string.Equals(callerOrg, config.OrganizationId, StringComparison.Ordinal))
            {
                return "org";
            }

            var rolesOk = EvaluateRule(config.Roles, callerRoles);
            if (!rolesOk && config.IsCheckBothRolesAndPermissions) return "roles";

            var permissionsOk = EvaluateRule(config.Permissions, callerPermissions);
            if (!permissionsOk && config.IsCheckBothRolesAndPermissions) return "permissions";

            // isCheckBothRolesAndPermissions true => both rules must pass; false => either is enough.
            var combined = config.IsCheckBothRolesAndPermissions
                ? rolesOk && permissionsOk
                : rolesOk || permissionsOk;

            return combined ? null : "match";
        }

        /// <summary>
        /// Evaluates a single role/permission rule. Empty <see cref="AuthRule.Items"/> means
        /// "no rule configured" and is treated as a pass.
        /// </summary>
        private static bool EvaluateRule(AuthRule rule, IReadOnlySet<string> callerValues)
        {
            if (rule is null || rule.Items is null || rule.Items.Count == 0) return true;

            return string.Equals(rule.Operator, "and", StringComparison.Ordinal)
                ? rule.Items.All(callerValues.Contains)
                : rule.Items.Any(callerValues.Contains);
        }

        // ---- claim readers ----

        public static string GetUserId(ClaimsPrincipal principal)
            => principal.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value ?? string.Empty;

        public static string GetOrganization(ClaimsPrincipal principal)
            => principal.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)?.Value ?? string.Empty;

        public static IReadOnlySet<string> GetRoles(ClaimsPrincipal principal)
            => principal.FindAll(BlocksContext.ROLES_CLAIM).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        public static IReadOnlySet<string> GetPermissions(ClaimsPrincipal principal)
            => principal.FindAll(BlocksContext.PERMISSION_CLAIM).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        // ---- DTOs (storage-agnostic value objects) ----

        /// <summary>
        /// One authorization rule (a roles rule or a permissions rule).
        /// <para><c>Operator</c> must be <c>"all"</c> or <c>"any"</c>; an empty <c>Items</c> list means
        /// "no rule configured" and is treated as a pass.</para>
        /// </summary>
        public sealed record AuthRule(string Operator, IReadOnlyList<string> Items);

        /// <summary>
        /// Plain value-object describing how a webhook (or any future caller) should be authorized.
        /// This type intentionally has no knowledge of how its data was stored on disk — the caller
        /// is responsible for parsing it from whatever source (BSON, JSON, request DTO, hardcoded, etc.)
        /// before handing it to <see cref="IsAuthorized"/> or <see cref="EvaluateAuthorization"/>.
        /// </summary>
        /// <param name="IsCheckBothRolesAndPermissions">
        /// <c>true</c> => caller must satisfy BOTH the Roles and Permissions rules (AND).
        /// <c>false</c> => caller may satisfy EITHER rule (OR).
        /// </param>
        public sealed record AuthorizationConfig(
            string OrganizationId,
            AuthRule Roles,
            AuthRule Permissions,
            bool IsCheckBothRolesAndPermissions)
        {
            /// <summary>Role/permission operators accepted on the wire.</summary>
            public static readonly IReadOnlySet<string> AllowedOperators = new HashSet<string>(StringComparer.Ordinal) { "and", "or" };
        }
    }
}