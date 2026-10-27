using DomainService.Shared;

namespace DomainService.Notification
{
    /// <summary>
    /// The receivers a sender asked for, read with the same rules a send without an audience uses: Roles (inside the
    /// sender's OrganizationIds when set) take the place of UserIds; OrganizationIds alone mean their members;
    /// subscription filters add their subscribers. It only ever sees users already in the audience, so it can narrow
    /// the audience but never widen it.
    /// </summary>
    internal sealed class SenderNarrowing
    {
        private readonly HashSet<string> _userIds;
        private readonly HashSet<string> _roles;
        private readonly HashSet<string> _organizationIds;
        private readonly HashSet<string> _filterUserIds;

        private readonly List<string> _roleHolders = [];
        private readonly List<string> _requestedUsers = [];
        private readonly List<string> _organizationMembers = [];
        private readonly List<string> _subscribers = [];

        private SenderNarrowing(NotifierPayload sender, HashSet<string> filterUserIds)
        {
            _userIds = AudienceRules.ToSet(sender.UserIds);
            _roles = AudienceRules.ToSet(sender.Roles);
            _organizationIds = AudienceRules.ToSet(sender.OrganizationIds);
            _filterUserIds = filterUserIds;
        }

        /// <summary>True when the sender named any receivers (user ids, roles, organizations or subscription filters).</summary>
        public static bool SenderGaveLists(NotifierPayload sender) =>
            sender.UserIds is { Count: > 0 } || sender.Roles is { Count: > 0 }
            || sender.OrganizationIds is { Count: > 0 } || sender.SubscriptionFilters is { Count: > 0 };

        /// <summary>Null when the sender named no receivers: then the audience is the whole answer.</summary>
        public static SenderNarrowing? For(NotifierPayload sender, HashSet<string> filterUserIds) =>
            SenderGaveLists(sender) ? new SenderNarrowing(sender, filterUserIds) : null;

        /// <summary>Sender user ids that are not in <paramref name="audienceUserIds"/> (unknown, inactive or outside the rules).</summary>
        public int CountRequestedOutside(HashSet<string> audienceUserIds) => _userIds.Count(id => !audienceUserIds.Contains(id));

        /// <summary>Records one audience user against each sender list.</summary>
        public void Observe(AudienceUser user)
        {
            if (_roles.Count > 0 && user.Roles.Any(entry => entry.Value is not null
                    && (_organizationIds.Count == 0 || _organizationIds.Contains(entry.Key))
                    && entry.Value.Any(_roles.Contains)))
                _roleHolders.Add(user.ItemId);
            if (_userIds.Contains(user.ItemId))
                _requestedUsers.Add(user.ItemId);
            if (_organizationIds.Count > 0 && user.OrganizationIds.Any(_organizationIds.Contains))
                _organizationMembers.Add(user.ItemId);
            if (_filterUserIds.Contains(user.ItemId))
                _subscribers.Add(user.ItemId);
        }

        /// <summary>The audience users the sender asked for.</summary>
        public List<string> Result()
        {
            List<string> named;
            if (_roles.Count > 0)
                // As without an audience: roles that match nobody leave the sender's user ids in place.
                named = _roleHolders.Count > 0 ? _roleHolders : _requestedUsers;
            else if (_userIds.Count == 0 && _organizationIds.Count > 0)
                named = _organizationMembers;
            else
                named = _requestedUsers;

            return named.Concat(_subscribers).Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
