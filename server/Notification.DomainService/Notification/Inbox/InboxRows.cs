using Blocks.Genesis.Entities.Logic;

namespace DomainService.Notification
{
    /// <summary>Shapes inbox rows for the viewer who reads them.</summary>
    public static class InboxRows
    {
        /// <summary>
        /// Sets <see cref="OfflineNotification.IsRead"/> for <paramref name="userId"/> and reduces ReadByUserIds to
        /// that user: an organization row's list holds the ids of every reader, which other users must not see.
        /// Clients that check "my id is in ReadByUserIds" keep working.
        /// </summary>
        public static List<OfflineNotification> ForViewer(List<OfflineNotification> rows, string userId)
        {
            foreach (var row in rows)
            {
                row.IsRead = row.ReadByUserIds?.Contains(userId) == true;
                row.ReadByUserIds = row.IsRead ? [userId] : [];
            }

            return rows;
        }
    }
}
