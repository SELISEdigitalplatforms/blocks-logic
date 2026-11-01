using Workflow.DomainService.Services;

namespace Workflow.DomainService.Nodes.TriggerWebhookV1
{
    public class TriggerWebhookV1Parameters
    {
        public string Path { get; set; } = string.Empty;
        public string ExecutionMode { get; set; } = "Test";
        public string HttpMethod { get; set; } = "POST";
        public string HttpResponseMode { get; set; } = "immediate";
        public string AuthType { get; set; } = "none";
        public string OrganizationId { get; set; } = "";
        public WorkflowAuthService.AuthorizationMode? AuthorizationMode { get; set; }
        public WorkflowAuthService.Rule? Roles { get; set; }
        public WorkflowAuthService.Rule? Permissions { get; set; }
        public string HttpResponseData { get; set; } = "all";

        /// <summary>
        /// <c>json</c> (legacy: a JSON object or array of objects only) or <c>anyContent</c>. Absent on every
        /// webhook saved before this setting existed, which keeps them on the legacy behaviour.
        /// See <see cref="WebhookIntakeSettings"/>.
        /// </summary>
        public string? BodyMode { get; set; }

        /// <summary>Any content: storage directory for uploaded files; blank = the configuration's root.</summary>
        public string FilesFolderId { get; set; } = "";

        /// <summary>Any content: storage configuration name; blank = the default configuration.</summary>
        public string StorageConfigurationName { get; set; } = "";

        /// <summary>Any content: request header names copied into the item's <c>headers</c>.</summary>
        public List<string> AllowedHeaders { get; set; } = new();
    }
}
