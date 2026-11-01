using Blocks.Genesis.Notifications;

namespace DomainService.Notification
{
    /// <summary>The SignalR groups a hub connection joins (names from genesis <see cref="NotificationGroups"/>).</summary>
    public static class NotificationHubGroups
    {
        /// <summary>
        /// For every tenant of <see cref="HubCaller.GroupTenantIds"/>: the tenant broadcast group and, with a user id,
        /// the user's group in it. Then one group per organization of the user, in <see cref="HubCaller.OrganizationTenantId"/>.
        /// </summary>
        public static IReadOnlyList<string> For(HubCaller caller, IReadOnlyCollection<string> organizationIds)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(organizationIds);

            var groups = new List<string>();
            var hasUser = !string.IsNullOrWhiteSpace(caller.UserId);
            foreach (var tenantId in caller.GroupTenantIds())
            {
                groups.Add(NotificationGroups.ForTenant(tenantId));
                if (hasUser)
                    groups.Add(NotificationGroups.ForTenantUser(tenantId, caller.UserId));
            }

            if (hasUser && caller.OrganizationTenantId() is { } organizationTenantId)
            {
                groups.AddRange(organizationIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => NotificationGroups.ForTenantOrg(organizationTenantId, id)));
            }

            return groups.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
