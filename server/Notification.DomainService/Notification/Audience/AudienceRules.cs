using Blocks.Genesis.Entities.Os;

namespace DomainService.Notification
{
    /// <summary>
    /// Decides whether one user is in a configuration's audience. Each non-empty rule list is one test; Match Any
    /// needs one test to pass, Match All needs every test to pass. Roles and Permissions are looked up only inside
    /// the audience organizations when that list is set.
    /// </summary>
    internal sealed class AudienceRules
    {
        private readonly bool _everyone;
        private readonly bool _matchAll;
        private readonly HashSet<string> _userIds;
        private readonly HashSet<string> _organizationIds;
        private readonly HashSet<string> _roles;
        private readonly HashSet<string> _permissions;
        private readonly HashSet<string>? _filterUserIds;
        private readonly IReadOnlyDictionary<string, HashSet<string>> _grantingRolesByOrganization;

        /// <param name="filterUserIds">Users matched by the audience filters; null when the audience has no filters.</param>
        /// <param name="grantingRolesByOrganization">Roles that grant one of the audience permissions, per organization.</param>
        public AudienceRules(NotificationAudience audience, HashSet<string>? filterUserIds,
            IReadOnlyDictionary<string, HashSet<string>> grantingRolesByOrganization)
        {
            // Rules win over EveryoneInTenant if bad data holds both: the narrower list is the safe one.
            _everyone = audience.EveryoneInTenant && !audience.HasRules();
            _matchAll = audience.Match == NotificationAudienceMatch.All;
            _userIds = ToSet(audience.UserIds);
            _organizationIds = ToSet(audience.OrganizationIds);
            _roles = ToSet(audience.Roles);
            _permissions = ToSet(audience.Permissions);
            _filterUserIds = filterUserIds;
            _grantingRolesByOrganization = grantingRolesByOrganization;
        }

        /// <summary>
        /// What the database can narrow without changing the answer: with Match All every set list must pass, and with
        /// Match Any a single list is the whole rule. Roles, Permissions and filters are always checked here.
        /// </summary>
        public AudienceUserQuery DatabaseQuery()
        {
            if (_everyone)
                return AudienceUserQuery.Everyone;
            if (_matchAll)
                return new AudienceUserQuery([.. _userIds], [.. _organizationIds]);

            var onlyUsers = _userIds.Count > 0 && _organizationIds.Count == 0 && !HasRoleOrPermissionOrFilterRules;
            var onlyOrganizations = _organizationIds.Count > 0 && _userIds.Count == 0 && !HasRoleOrPermissionOrFilterRules;
            return new AudienceUserQuery(onlyUsers ? [.. _userIds] : [], onlyOrganizations ? [.. _organizationIds] : []);
        }

        private bool HasRoleOrPermissionOrFilterRules => _roles.Count > 0 || _permissions.Count > 0 || _filterUserIds is not null;

        public bool Includes(AudienceUser user)
        {
            if (_everyone)
                return true;

            var results = SetRuleResults(user);
            return _matchAll ? results.All(passed => passed) : results.Any(passed => passed);
        }

        /// <summary>One result per non-empty rule list, computed lazily so Any and All stop early.</summary>
        private IEnumerable<bool> SetRuleResults(AudienceUser user)
        {
            if (_userIds.Count > 0)
                yield return _userIds.Contains(user.ItemId);
            if (_organizationIds.Count > 0)
                yield return user.OrganizationIds.Any(_organizationIds.Contains);
            if (_roles.Count > 0)
                yield return HoldsRole(user);
            if (_permissions.Count > 0)
                yield return HoldsPermission(user);
            if (_filterUserIds is not null)
                yield return _filterUserIds.Contains(user.ItemId);
        }

        private bool HoldsRole(AudienceUser user) =>
            InScope(user.Roles).Any(entry => entry.Value.Any(_roles.Contains));

        /// <summary>
        /// Same two paths the platform checks on a protected call: the permission is in the user's own list for the
        /// organization, or a permission row of that organization lists one of the user's roles there. Parent roles do
        /// not pass permissions down (a token carries only the user's own roles).
        /// </summary>
        private bool HoldsPermission(AudienceUser user) =>
            InScope(user.Permissions).Any(entry => entry.Value.Any(_permissions.Contains))
            || InScope(user.Roles).Any(entry =>
                _grantingRolesByOrganization.TryGetValue(entry.Key, out var granting) && entry.Value.Any(granting.Contains));

        private IEnumerable<KeyValuePair<string, List<string>>> InScope(IReadOnlyDictionary<string, List<string>> byOrganization) =>
            byOrganization.Where(entry => entry.Value is not null
                && (_organizationIds.Count == 0 || _organizationIds.Contains(entry.Key)));

        internal static HashSet<string> ToSet(IEnumerable<string>? values) =>
            values is null ? [] : values.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.Ordinal);
    }
}
