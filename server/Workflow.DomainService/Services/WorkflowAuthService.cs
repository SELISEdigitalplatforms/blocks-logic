using System.Security.Claims;
using Blocks.Genesis;
using Common.InternalService.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// Webhook-trigger inbound auth. Token validation, claim reading and rule evaluation live in the
    /// shared <see cref="IEndpointAccessAuthorizer"/> / <see cref="EndpointAccessEvaluator"/> (Common) so
    /// the proxy gateway and any future tenant-published endpoint enforce exactly the same semantics;
    /// this class keeps the webhook's own wire shape (<see cref="AuthorizationConfig"/>,
    /// <see cref="AuthorizationMode"/>, <see cref="Rule"/>) and maps it onto an
    /// <see cref="EndpointAccessPolicy"/>.
    /// </summary>
    public class WorkflowAuthService : IWorkflowAuthService
    {
        private readonly IEndpointAccessAuthorizer _accessAuthorizer;
        private readonly ILogger<WorkflowAuthService> _logger;
        private readonly IDelegatedTokenProvider _delegatedTokenProvider;

        public WorkflowAuthService(
            IEndpointAccessAuthorizer accessAuthorizer,
            ILogger<WorkflowAuthService> logger,
            IDelegatedTokenProvider delegatedTokenProvider)
        {
            _accessAuthorizer = accessAuthorizer;
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
            var (principal, _) = await _accessAuthorizer.ValidateTokenAsync(request, tenantId);
            if (principal is null)
            {
                _logger.LogWarning("Workflow webhook authentication failed. TenantId={TenantId}", tenantId);
                return false;
            }

            AttachUser(request, principal);
            return true;
        }

        public async Task<(bool isAuthorized, BlocksContext? context)> IsAuthorized(HttpRequest request, string tenantId, AuthorizationConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var decision = await _accessAuthorizer.AuthorizeAsync(request, tenantId, config.ToPolicy());
            switch (decision.Status)
            {
                case EndpointAccessStatus.Unauthenticated:
                    _logger.LogWarning("Workflow webhook authorization failed (unauthenticated). TenantId={TenantId}", tenantId);
                    return (false, null);
                case EndpointAccessStatus.Forbidden:
                    _logger.LogWarning("Workflow webhook authorization failed. TenantId={TenantId}", tenantId);
                    return (false, null);
                default:
                    return (true, decision.Context);
            }
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
        /// Pure evaluation of org + roles + permissions rules against the validated caller. Kept for the
        /// webhook's callers and tests; the semantics are those of <see cref="EndpointAccessEvaluator.Evaluate"/>.
        /// </summary>
        public static bool EvaluateAuthorization(ClaimsPrincipal principal, AuthorizationConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return EndpointAccessEvaluator.Evaluate(principal, config.ToPolicy());
        }

        // ---- claim readers (thin forwards so existing callers keep compiling) ----

        public static string GetUserId(ClaimsPrincipal principal) => EndpointAccessEvaluator.GetUserId(principal);

        public static string GetOrganization(ClaimsPrincipal principal) => EndpointAccessEvaluator.GetOrganization(principal);

        public static IReadOnlySet<string> GetRoles(ClaimsPrincipal principal) => EndpointAccessEvaluator.GetRoles(principal);

        public static IReadOnlySet<string> GetPermissions(ClaimsPrincipal principal) => EndpointAccessEvaluator.GetPermissions(principal);

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
        /// <c>isRolePermission</c> fields with an explicit choice.
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
            /// <summary>Either the Roles or the Permissions rule must pass (OR).</summary>
            RolesOrPermissions = 3,
        }

        /// <summary>
        /// Plain value-object describing how a webhook (or any future caller) should be authorized.
        /// Carries the on-wire <see cref="Rule"/> shape (<c>{ mode, values }</c>) for roles and
        /// permissions; the service resolves the AND/OR semantic per rule from <see cref="Rule.Mode"/>.
        /// A null/empty rule means "no rule configured" and is treated as a pass.
        /// </summary>
        /// <param name="Mode">
        /// Which rule(s) must pass: <see cref="AuthorizationMode.RolesOnly"/>,
        /// <see cref="AuthorizationMode.PermissionsOnly"/>,
        /// <see cref="AuthorizationMode.RolesAndPermissions"/> or
        /// <see cref="AuthorizationMode.RolesOrPermissions"/>.
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
                nameof(AuthorizationMode.RolesOrPermissions),
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

            /// <summary>
            /// The shared policy this webhook config denotes. <c>RolesOnly</c> / <c>PermissionsOnly</c> drop
            /// the other rule entirely (it never applied under those modes); the two-rule modes keep both
            /// and pick the combinator.
            /// </summary>
            public EndpointAccessPolicy ToPolicy()
            {
                var roles = Mode is AuthorizationMode.PermissionsOnly ? null : Roles;
                var permissions = Mode is AuthorizationMode.RolesOnly ? null : Permissions;

                return new EndpointAccessPolicy
                {
                    Kind = EndpointAccessKind.BlocksToken,
                    OrganizationId = OrganizationId ?? string.Empty,
                    Roles = Rule.ToAccessRule(roles),
                    Permissions = Rule.ToAccessRule(permissions),
                    Combine = Mode == AuthorizationMode.RolesAndPermissions
                        ? EndpointAccessCombine.And
                        : EndpointAccessCombine.Or,
                };
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

            internal static EndpointAccessRule ToAccessRule(Rule? rule) => new()
            {
                Mode = EndpointAccessRule.NormalizeMode(rule?.Mode),
                Values = rule?.Values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? new List<string>(),
            };
        }
    }
}
