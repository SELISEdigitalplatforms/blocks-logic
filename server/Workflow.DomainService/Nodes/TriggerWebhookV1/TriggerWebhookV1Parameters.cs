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
        /// <c>standard</c> (any body type, one item per request) or <c>jsonLegacy</c> (a JSON object or array of
        /// objects only). Absent on every webhook saved before this setting existed, which keeps them on legacy
        /// JSON. The retired <c>bodyMode</c> and <c>acceptedBodyTypes</c> are ignored if still saved.
        /// See <see cref="WebhookIntakeSettings"/>.
        /// </summary>
        public string? RequestFormat { get; set; }
    }
}
