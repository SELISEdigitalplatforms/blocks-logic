using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;
using DomainService.Shared;

namespace DomainService.Notification
{
    /// <summary>
    /// Final receivers of a send whose configuration has an audience: every active audience user, or, when the sender
    /// named receivers, only the audience users it named. One pass over the active users of the tenant.
    /// </summary>
    public sealed class NotificationAudienceResolver : INotificationAudienceResolver
    {
        private readonly INotificationAudienceRepository _audienceRepository;
        private readonly INotificationRepository _notificationRepository;

        public NotificationAudienceResolver(INotificationAudienceRepository audienceRepository, INotificationRepository notificationRepository)
        {
            _audienceRepository = audienceRepository;
            _notificationRepository = notificationRepository;
        }

        public Task<AudienceResolution> ResolveAsync(NotificationAudience audience, NotifierPayload sender, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audience);
            ArgumentNullException.ThrowIfNull(sender);
            return ResolveCoreAsync(audience, sender, cancellationToken);
        }

        private async Task<AudienceResolution> ResolveCoreAsync(NotificationAudience audience, NotifierPayload sender, CancellationToken cancellationToken)
        {
            var audienceFilterUsers = audience.Filters is { Count: > 0 } ? await SubscriberIdsAsync(audience.Filters) : null;
            var grantingRoles = await GrantingRolesByOrganizationAsync(audience.Permissions, cancellationToken);
            var rules = new AudienceRules(audience, audienceFilterUsers, grantingRoles);

            var senderFilterUsers = sender.SubscriptionFilters is { Count: > 0 } ? await SubscriberIdsAsync(sender.SubscriptionFilters) : [];
            var narrowing = SenderNarrowing.For(sender, senderFilterUsers);

            var audienceUserIds = new HashSet<string>(StringComparer.Ordinal);
            await foreach (var user in _audienceRepository.GetActiveUsersAsync(rules.DatabaseQuery(), cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(user.ItemId) || !rules.Includes(user) || !audienceUserIds.Add(user.ItemId))
                    continue;
                narrowing?.Observe(user);
            }

            var receivers = narrowing?.Result() ?? [.. audienceUserIds];
            var dropped = narrowing?.CountRequestedOutside(audienceUserIds) ?? 0;
            return new AudienceResolution(receivers, audienceUserIds.Count, dropped);
        }

        /// <summary>Roles that grant one of the permissions, per organization. Empty when no permission rule is set.</summary>
        private async Task<IReadOnlyDictionary<string, HashSet<string>>> GrantingRolesByOrganizationAsync(
            List<string>? permissions, CancellationToken cancellationToken)
        {
            var resources = AudienceRules.ToSet(permissions);
            if (resources.Count == 0)
                return new Dictionary<string, HashSet<string>>();

            var grants = await _audienceRepository.GetRoleGrantsAsync(resources, cancellationToken);
            return grants
                .GroupBy(g => g.OrganizationId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => AudienceRules.ToSet(group.SelectMany(g => g.Roles)), StringComparer.Ordinal);
        }

        /// <summary>
        /// Users with a live hub connection subscribed to one of the filters: the same match a filter notification
        /// uses (exact, or the subscription leaves Value, or ActionName and Value, empty).
        /// </summary>
        private async Task<HashSet<string>> SubscriberIdsAsync(List<SubscriptionFilter> filters)
        {
            var connectionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var filter in filters.Where(f => f is not null))
            {
                var context = filter.Context;
                var action = filter.ActionName;
                var value = filter.Value;
                var subscriptions = await _notificationRepository.GetItemsAsync<NotificationSubscription>(p =>
                    (p.Context == context && p.ActionName == action && p.Value == value)
                    || (p.Context == context && p.ActionName == action && p.Value == "")
                    || (p.Context == context && p.ActionName == "" && p.Value == ""));
                connectionIds.UnionWith(subscriptions.Select(s => s.ConnectionId).Where(id => !string.IsNullOrWhiteSpace(id)));
            }

            if (connectionIds.Count == 0)
                return [];

            var ids = connectionIds.ToList();
            var connections = await _notificationRepository.GetItemsAsync<NotificationConnection>(c => ids.Contains(c.ConnectionId));
            return AudienceRules.ToSet(connections.Select(c => c.UserId!));
        }
    }
}
