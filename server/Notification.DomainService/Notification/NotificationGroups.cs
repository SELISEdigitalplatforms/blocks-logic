namespace DomainService.Notification
{
    /// <summary>SignalR group names. Every hub connection joins the group of its token's tenant.</summary>
    public static class NotificationGroups
    {
        public static string ForTenant(string tenantId) => $"tenant:{tenantId}";
    }
}
