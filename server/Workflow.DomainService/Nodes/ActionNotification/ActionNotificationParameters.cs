namespace Workflow.DomainService.Nodes.ActionNotification
{
    /// <summary>
    /// Send Notification step. Every text value may hold <c>{{…}}</c> expressions, resolved per item.
    /// The recipient fields of the other receiver types stay in the saved node and are ignored at send
    /// time, so switching the configuration back and forth keeps what the user typed.
    /// </summary>
    public class ActionNotificationParameters
    {
        /// <summary>Name of a notification configuration of the workflow's project.</summary>
        public string ConfigurationName { get; set; } = string.Empty;

        /// <summary>Users receiver: user ids. An entry that resolves to a JSON array of text adds each one.</summary>
        public List<string> UserIds { get; set; } = new();

        /// <summary>Users receiver: roles, turned into the users holding them by the Notifier.</summary>
        public List<string> Roles { get; set; } = new();

        /// <summary>Users receiver: organizations; scope the roles, or every member when no user or role is set.</summary>
        public List<string> OrganizationIds { get; set; } = new();

        /// <summary>Filter receiver: the subscriptions to reach.</summary>
        public List<ActionNotificationFilter> SubscriptionFilters { get; set; } = new();

        /// <summary>JSON template; sent as the notification's denormalized payload.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Store the payload as an object instead of a JSON string (only with persistence on).</summary>
        public bool SaveAsObject { get; set; }

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
