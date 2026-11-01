using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;
using MongoDB.Driver;

namespace DomainService.Notification
{
    /// <summary>Which <c>OfflineNotifications</c> rows a viewer may see. One place, so reads and mark-as-read agree.</summary>
    public static class InboxFilters
    {
        private static readonly FilterDefinitionBuilder<OfflineNotification> Filter = Builders<OfflineNotification>.Filter;

        /// <summary>
        /// The viewer's own rows, plus organization rows ("All users": no user id, organization of the viewer). With
        /// <paramref name="includeLegacyBroadcast"/> also the old broadcast rows (no user, no organization), which
        /// the subscription-filter read and mark-all have always covered. An impersonated viewer sees only rows of
        /// the project or rows without a project.
        /// </summary>
        public static FilterDefinition<OfflineNotification> Visible(InboxViewer viewer, bool includeLegacyBroadcast)
        {
            ArgumentNullException.ThrowIfNull(viewer);

            var parts = new List<FilterDefinition<OfflineNotification>>
            {
                Filter.Eq(n => n.Payload.UserId, viewer.UserId),
            };

            if (viewer.OrganizationIds.Count > 0)
            {
                parts.Add(NoUser() & Filter.In(n => n.OrganizationId, viewer.OrganizationIds));
            }

            if (includeLegacyBroadcast)
            {
                parts.Add(NoUser()
                    & Filter.Eq(n => n.OrganizationId, null)
                    & Filter.Eq(n => n.Payload.NotificationType, NotificationReceiverTypes.BroadcastReceiverType.ToString()));
            }

            var visible = Filter.Or(parts);
            return viewer.ProjectTenantId is { } projectTenantId
                ? visible & Filter.Or(Filter.Eq(n => n.TenantId, projectTenantId), Filter.Eq(n => n.TenantId, null))
                : visible;
        }

        /// <summary>Rows the viewer has not read yet.</summary>
        public static FilterDefinition<OfflineNotification> Unread(InboxViewer viewer) =>
            Filter.Not(Filter.AnyEq(n => n.ReadByUserIds, viewer.UserId));

        // Null also matches a missing field.
        private static FilterDefinition<OfflineNotification> NoUser() =>
            Filter.Or(Filter.Eq(n => n.Payload.UserId, null), Filter.Eq(n => n.Payload.UserId, string.Empty));
    }
}
