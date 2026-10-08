using System.Security.Claims;
using Blocks.Genesis;

namespace Common.InternalService.Access
{
    /// <summary>
    /// Pure evaluation of an <see cref="EndpointAccessPolicy"/> against an already-validated caller, plus
    /// the claim readers every access check shares. No I/O, so it is unit-testable in isolation.
    /// </summary>
    public static class EndpointAccessEvaluator
    {
        /// <summary>
        /// <c>true</c> when <paramref name="principal"/> satisfies <paramref name="policy"/>. A public
        /// policy always passes. For a token policy the organization rule (if any) must match, and then:
        /// no configured rule ⇒ pass; one configured rule ⇒ that rule decides; both configured ⇒ combined
        /// by <see cref="EndpointAccessPolicy.Combine"/>.
        /// </summary>
        public static bool Evaluate(ClaimsPrincipal principal, EndpointAccessPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(principal);
            ArgumentNullException.ThrowIfNull(policy);

            if (policy.IsPublic)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(policy.OrganizationId))
            {
                // A missing / empty org claim means the caller sits in the tenant's default organization.
                var callerOrg = GetOrganization(principal);
                if (string.IsNullOrEmpty(callerOrg))
                {
                    callerOrg = "default";
                }

                if (!string.Equals(callerOrg, policy.OrganizationId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            var configured = new List<bool>(2);
            if (policy.Roles.IsConfigured)
            {
                configured.Add(policy.Roles.IsSatisfiedBy(GetRoles(principal)));
            }

            if (policy.Permissions.IsConfigured)
            {
                configured.Add(policy.Permissions.IsSatisfiedBy(GetPermissions(principal)));
            }

            if (configured.Count == 0)
            {
                return true;
            }

            return policy.Combine == EndpointAccessCombine.And
                ? configured.All(pass => pass)
                : configured.Any(pass => pass);
        }

        /// <summary>
        /// Why <paramref name="principal"/> fails <paramref name="policy"/> — which organization, roles or
        /// permissions are missing — or <c>null</c> when it passes. For the server log only: the 403 answer
        /// stays generic, because naming the rules tells any caller which role to go after (FN-36).
        /// </summary>
        public static string? ExplainDenial(ClaimsPrincipal principal, EndpointAccessPolicy policy)
        {
            if (Evaluate(principal, policy)) return null;

            if (!string.IsNullOrEmpty(policy.OrganizationId))
            {
                var callerOrg = GetOrganization(principal);
                if (string.IsNullOrEmpty(callerOrg)) callerOrg = "default";
                if (!string.Equals(callerOrg, policy.OrganizationId, StringComparison.Ordinal))
                {
                    return $"organization '{policy.OrganizationId}' required, caller is in '{callerOrg}'";
                }
            }

            var reasons = new List<string>(2);
            if (policy.Roles.IsConfigured && !policy.Roles.IsSatisfiedBy(GetRoles(principal)))
            {
                reasons.Add(DescribeMissing("role", policy.Roles, GetRoles(principal)));
            }

            if (policy.Permissions.IsConfigured && !policy.Permissions.IsSatisfiedBy(GetPermissions(principal)))
            {
                reasons.Add(DescribeMissing("permission", policy.Permissions, GetPermissions(principal)));
            }

            var combine = policy.Combine == EndpointAccessCombine.And ? "all rules" : "any rule";
            return reasons.Count == 0 ? "the access rules are not met" : $"{string.Join("; ", reasons)} ({combine} must pass)";
        }

        private static string DescribeMissing(string kind, EndpointAccessRule rule, IReadOnlySet<string> held) =>
            rule.RequiresAll
                ? $"missing {kind}(s): {string.Join(", ", rule.Values.Where(v => !held.Contains(v)))}"
                : $"needs one {kind} of: {string.Join(", ", rule.Values)}";

        // ---- claim readers ----

        public static string GetUserId(ClaimsPrincipal principal)
            => principal?.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value ?? string.Empty;

        public static string GetOrganization(ClaimsPrincipal principal)
            => principal?.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)?.Value ?? string.Empty;

        /// <summary>
        /// Reads the caller's roles via the identity's <c>RoleClaimType</c> (so JWT inbound-claim remapping
        /// cannot cause silent misses) plus the literal <see cref="BlocksContext.ROLES_CLAIM"/> for identities
        /// built with <c>MapInboundClaims = false</c> or by hand.
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
            => (principal?.FindAll(BlocksContext.PERMISSION_CLAIM) ?? Enumerable.Empty<Claim>())
                .Select(c => c.Value)
                .ToHashSet(StringComparer.Ordinal);

        public static string GetClaimValue(ClaimsPrincipal principal, string claimType)
            => principal?.FindFirst(claimType)?.Value ?? string.Empty;

        /// <summary>The standard JWT <c>exp</c> claim as a <see cref="DateTime"/>, or <see cref="DateTime.MinValue"/>.</summary>
        public static DateTime GetExpireOn(ClaimsPrincipal principal)
        {
            var expireOnValue = principal?.FindFirst(BlocksContext.EXPIRE_ON_CLAIM)?.Value;
            return DateTime.TryParse(
                expireOnValue,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var expireOn)
                ? expireOn
                : DateTime.MinValue;
        }

        public static bool GetImpersonated(ClaimsPrincipal principal)
            => principal?.FindFirst(BlocksContext.IMPERSONATED_CLAIM)?.Value == "true";
    }
}
