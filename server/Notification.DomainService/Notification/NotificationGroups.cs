namespace DomainService.Notification
{
    /// <summary>SignalR group names. Every hub connection joins the group of its token's tenant.</summary>
    public static class NotificationGroups
    {
        public static string ForTenant(string tenantId) => $"tenant:{tenantId}";

        /// <summary>
        /// The group of one root tenant user while they work in a project (impersonated). A sender that uses the
        /// project key for that user's own action reaches them here; their connection row lives in the root DB.
        /// </summary>
        public static string ForTenantUser(string tenantId, string userId) => $"tenant:{tenantId}:user:{userId}";
    }
}
