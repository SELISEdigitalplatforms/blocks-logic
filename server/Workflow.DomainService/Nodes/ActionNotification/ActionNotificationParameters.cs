namespace Workflow.DomainService.Nodes.ActionNotification
{
    /// <summary>
    /// Send Notification step. Every text value may hold <c>{{…}}</c> expressions, resolved per item.
    /// Who receives the notification and on which channels is decided by the configuration, not here.
    /// </summary>
    public class ActionNotificationParameters
    {
        /// <summary>Name of a notification configuration of the workflow's project.</summary>
        public string ConfigurationName { get; set; } = string.Empty;

        /// <summary>The users the event is about. An entry that resolves to a JSON array of text adds each one.</summary>
        public List<string> UserIds { get; set; } = new();

        /// <summary>The send's organization, used by "All users" and "Targeted" rules. Blank means the default organization.</summary>
        public string OrganizationId { get; set; } = string.Empty;

        /// <summary>JSON template; sent as the notification payload.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Carried with the message so a page can match it; does not choose receivers.</summary>
        public List<ActionNotificationFilter> SubscriptionFilters { get; set; } = new();

        /// <summary>Optional; empty means the configuration name.</summary>
        public string ResponseKey { get; set; } = string.Empty;

        /// <summary>Optional; empty means <c>"Sent"</c>.</summary>
        public string ResponseValue { get; set; } = string.Empty;
    }

    public class ActionNotificationFilter
    {
        public string Context { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
