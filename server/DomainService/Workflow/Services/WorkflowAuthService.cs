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
        private readonly IDelegatedTokenProvider _delegatedTokenProvider;
        private const string PublicCertCachePrefix = "tetocertpublic::";

        public WorkflowAuthService(
            ITenants tenants,
            ICacheClient cacheClient,
            ILogger<WorkflowAuthService> logger,
            IDelegatedTokenProvider delegatedTokenProvider)
        {
            _tenants = tenants;
            _cacheClient = cacheClient;
            _logger = logger;
            _delegatedTokenProvider = delegatedTokenProvider;
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
            var (principal, _) = await ValidateTokenAsync(request, tenantId);
            if (principal is null)
            {
                _logger.LogWarning("Workflow webhook authentication failed. TenantId={TenantId}", tenantId);
                return false;
            }

            AttachUser(request, principal);
            return true;
        }

        public async Task<bool> IsAuthorized(HttpRequest request, string tenantId, AuthorizationConfig config)
        {
            // Step 1 — authenticate first. Strict ordering: RBAC never runs for an unauthenticated caller.
            var (principal, _) = await ValidateTokenAsync(request, tenantId);
            if (principal is null)
            {
                _logger.LogWarning("Workflow webhook authorization failed (unauthenticated). TenantId={TenantId}", tenantId);
                return false;
            }

            // Step 2 — authorize org + roles + permissions combined by matchType.
            var isAuthorized = EvaluateAuthorization(principal, config);
            if (!isAuthorized)
            {
                _logger.LogWarning("Workflow webhook authorization failed. TenantId={TenantId}, UserId={UserId}",
                    tenantId, GetUserId(principal));
                return false;
            }

            AttachUser(request, principal);
            return true;
        }

        /// <summary>
        /// Best-effort: returns a Blocks-delegated bearer token for the current ambient context, or
        /// <c>null</c> when no delegation grant is available. <see cref="IDelegatedTokenProvider.GetTokenAsync"/>
        /// only <b>redeems</b> an existing delegation grant (<c>DelegatedTokenContext.Current</c>) — it does not
        /// mint one from scratch. After a successful webhook auth the validated principal is assigned to
        /// <c>HttpContext.User</c> so Genesis can mint a grant on send (or the in-process hop). Callers
        /// must treat <c>null</c> as "omit the Authorization header", not as an error.
        /// </summary>
        public Task<string?> CreateBlocksAuthorizationTokenAsync(CancellationToken ct = default)
            => _delegatedTokenProvider.GetTokenAsync(ct);

        /// <summary>
        /// Validates the bearer token of a webhook request against the tenant's public cert
        /// and returns the resulting <see cref="ClaimsPrincipal"/> plus the raw token string.
        /// Returns <c>(null, null)</c> on any failure (missing token, unknown tenant, invalid
        /// signature, expired, etc.). Validation runs exactly once per request; both
        /// IsAuthenticated and IsAuthorized reuse this.
        /// </summary>
        private async Task<(ClaimsPrincipal? Principal, string? RawToken)> ValidateTokenAsync(HttpRequest request, string tenantId)
        {
            var tenant = _tenants.GetTenantByID(tenantId);
            if (tenant == null) return (null, null);

            var (token, _) = TokenHelper.GetToken(request, _tenants);
            if (string.IsNullOrEmpty(token)) return (null, null);

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
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
                var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out _);
                return (principal, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow webhook token validation threw. TenantId={TenantId}", tenantId);
                return (null, null);
            }
        }

        /// <summary>
        /// Pure evaluation of org + roles + permissions rules against the validated caller.
        /// Storage-agnostic: takes the on-wire <see cref="Rule"/> shape (<c>{ mode, values }</c>)
        /// and resolves the AND/OR semantic per rule from <see cref="Rule.Mode"/>.
        /// <para>Returns <c>true</c> when the caller satisfies every configured rule,
        /// <c>false</c> otherwise.</para>
        /// </summary>
        public static bool EvaluateAuthorization(ClaimsPrincipal principal, AuthorizationConfig config)
        {
            var callerOrg = GetOrganization(principal);

            // Organization: "default" sentinel = caller's own org (from JWT) is always accepted.
            if (!string.Equals(callerOrg, config.OrganizationId, StringComparison.Ordinal))
            {
                return false;
            }

            // Mode decides which rule(s) the caller must satisfy.
            // Each rule passes when null/empty (no rule configured).
            return config.Mode switch
            {
                AuthorizationMode.RolesOnly => Satisfies(GetRoles(principal), config.Roles),
                AuthorizationMode.PermissionsOnly => Satisfies(GetPermissions(principal), config.Permissions),
                AuthorizationMode.RolesAndPermissions => Satisfies(GetRoles(principal), config.Roles)
                    && Satisfies(GetPermissions(principal), config.Permissions),
                _ => false,
            };
        }

        /// <summary>
        /// True if <paramref name="rule"/> is null/empty (no rule configured)
        /// or the caller satisfies the rule. <see cref="Rule.Mode"/> = <c>"and"</c>
        /// requires the caller to hold every value; any other value (including <c>null</c>)
        /// requires at least one.
        /// </summary>
        private static bool Satisfies(IReadOnlySet<string> callerValues, Rule? rule)
        {
            if (rule is null || rule.Values is null || rule.Values.Count == 0) return true;
            return string.Equals(rule.Mode, "and", StringComparison.Ordinal)
                ? rule.Values.All(callerValues.Contains)
                : rule.Values.Any(callerValues.Contains);
        }

        // ---- claim readers ----

        public static string GetUserId(ClaimsPrincipal principal)
            => principal.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value ?? string.Empty;

        public static string GetOrganization(ClaimsPrincipal principal)
            => principal.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)?.Value ?? string.Empty;

        /// <summary>
        /// Reads the caller's roles. Mirrors <c>Blocks.Genesis</c>
        /// (<c>BlocksContext.CreateFromClaimsIdentity</c> and
        /// <c>JwtBearerAuthenticationExtension.StoreThirdPartyBlocksContextActivity</c>):
        /// roles are read via the identity's <c>RoleClaimType</c> so JWT inbound-claim remapping
        /// (e.g. "roles" -> <see cref="ClaimTypes.Role"/>) cannot cause silent misses.
        /// The literal <c>BlocksContext.ROLES_CLAIM</c> fallback covers callers that disabled
        /// <c>JwtSecurityTokenHandler.MapInboundClaims</c> or built identities by hand.
        /// </summary>
        public static IReadOnlySet<string> GetRoles(ClaimsPrincipal principal)
        {
            var identity = principal?.Identity as ClaimsIdentity;
            var byRoleClaimType = identity is null
                ? (principal?.FindAll(ClaimsIdentity.DefaultRoleClaimType) ?? Enumerable.Empty<Claim>())
                : identity.FindAll(identity.RoleClaimType);

            return byRoleClaimType
                .Concat(principal?.FindAll(BlocksContext.ROLES_CLAIM) ?? Enumerable.Empty<Claim>())
                .Select(c => c.Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        public static IReadOnlySet<string> GetPermissions(ClaimsPrincipal principal)
            => principal.FindAll(BlocksContext.PERMISSION_CLAIM).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// Puts the validated caller on the request so Genesis <c>BlocksContext.GetContext()</c>
        /// and <c>IDelegationGrantFactory.CreateForSendAsync</c> see an authenticated user
        /// (webhooks are anonymous, so JWT middleware never populated <c>HttpContext.User</c>).
        /// </summary>
        private static void AttachUser(HttpRequest request, ClaimsPrincipal principal)
        {
            if (request.HttpContext is not null)
            {
                request.HttpContext.User = principal;
            }
        }

        /// <summary>
        /// How the Roles and Permissions rules combine to authorize the caller.
        /// Replaces the legacy boolean <c>isCheckBothRolesAndPermissions</c> /
        /// <c>isRolePermission</c> fields with an explicit three-way choice.
        /// Wire values are the C# enum names verbatim.
        /// </summary>
        public enum AuthorizationMode
        {
            /// <summary>Only the Roles rule applies.</summary>
            RolesOnly = 0,
            /// <summary>Only the Permissions rule applies.</summary>
            PermissionsOnly = 1,
            /// <summary>Both the Roles and Permissions rules must pass (AND).</summary>
            RolesAndPermissions = 2,
        }

        /// <summary>
        /// Plain value-object describing how a webhook (or any future caller) should be authorized.
        /// Carries the on-wire <see cref="Rule"/> shape (<c>{ mode, values }</c>) for roles and
        /// permissions; the service resolves the AND/OR semantic per rule from <see cref="Rule.Mode"/>.
        /// A null/empty rule means "no rule configured" and is treated as a pass.
        /// </summary>
        /// <param name="Mode">
        /// Which rule(s) must pass: <see cref="AuthorizationMode.RolesOnly"/>,
        /// <see cref="AuthorizationMode.PermissionsOnly"/>, or
        /// <see cref="AuthorizationMode.RolesAndPermissions"/>.
        /// </param>
        public sealed record AuthorizationConfig(
            string OrganizationId,
            Rule? Roles,
            Rule? Permissions,
            AuthorizationMode Mode)
        {
            /// <summary>Authorization-mode identifiers accepted on the wire (= the C# enum names).</summary>
            public static readonly IReadOnlySet<string> AllowedAuthorizationModes = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(AuthorizationMode.RolesOnly),
                nameof(AuthorizationMode.PermissionsOnly),
                nameof(AuthorizationMode.RolesAndPermissions),
            };

            /// <summary>
            /// Parses the wire string into an <see cref="AuthorizationMode"/>, returning
            /// <c>null</c> when the value is unrecognised. Accepts only the C# enum names.
            /// </summary>
            public static AuthorizationMode? TryParseAuthorizationMode(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                return Enum.TryParse<AuthorizationMode>(value.Trim(), ignoreCase: false, out var parsed)
                    ? parsed
                    : null;
            }
        }

        /// <summary>
        /// On-wire shape for one role/permission rule: <c>{ mode: "and"|"or", values: string[] }</c>.
        /// Empty or null <see cref="Values"/> means "no rule configured" (treated as a pass).
        /// </summary>
        public sealed class Rule
        {
            /// <summary><c>"and"</c> = every value required; anything else (e.g. <c>"or"</c>) = at least one.</summary>
            public string? Mode { get; set; }

            /// <summary>Role slugs or permission resource keys. Empty or null = no rule.</summary>
            public List<string>? Values { get; set; }
        }
    }
}