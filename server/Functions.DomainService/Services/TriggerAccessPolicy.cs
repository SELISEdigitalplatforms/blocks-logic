using Common.InternalService.Access;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// A deployed version's <see cref="TriggerConfig"/> as the <see cref="EndpointAccessPolicy"/> the
    /// shared <see cref="IEndpointAccessAuthorizer"/> enforces on <c>/api/fn/…</c>.
    /// <para>
    /// Functions store their "Who can call it" in their own shape (it predates the shared policy),
    /// so this is the one translation between the two. It is deliberately a pure function of the
    /// trigger: the authorizer then does exactly what it does for a proxy gateway route — public
    /// passes without looking at credentials, otherwise the bearer token is validated against the
    /// tenant's certificate and the rules evaluated — and the two data planes cannot drift apart
    /// in how they read a token.
    /// </para>
    /// </summary>
    public static class TriggerAccessPolicy
    {
        public static EndpointAccessPolicy From(TriggerConfig trigger)
        {
            ArgumentNullException.ThrowIfNull(trigger);

            if (trigger.AuthMode == AuthMode.Public)
            {
                // Rules left over from an earlier token configuration must not resurrect: the
                // policy is public, full stop. Clearing them here mirrors what the interface
                // does when the tenant switches to Public.
                return EndpointAccessPolicy.AllowPublic();
            }

            return new EndpointAccessPolicy
            {
                Kind = EndpointAccessKind.BlocksToken,
                Roles = Rule(trigger.Roles, trigger.RoleMatch),
                Permissions = Rule(trigger.Permissions, trigger.PermissionMatch),
                Combine = trigger.Combine == AccessCombine.And
                    ? EndpointAccessCombine.And
                    : EndpointAccessCombine.Or,
            };
        }

        private static EndpointAccessRule Rule(IEnumerable<string>? values, MatchMode mode) => new()
        {
            Mode = mode == MatchMode.All ? EndpointAccessRule.ModeAll : EndpointAccessRule.ModeAny,
            // Trimmed and de-duplicated the way the shared rule documents its values; an empty
            // entry cannot be held by anyone and would turn "any of" into "none of".
            Values = (values ?? [])
                .Select(v => v?.Trim() ?? string.Empty)
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
        };
    }
}
