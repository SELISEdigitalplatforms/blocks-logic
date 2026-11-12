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

        /// <summary>
        /// Any content: the body types this webhook accepts (<c>json</c>, <c>form</c>, <c>multipart</c>,
        /// <c>text</c>, <c>binary</c>); empty accepts every type (SPEC10 D6). Files always go to Storage's default
        /// folder and configuration, and every non-sensitive header is copied, so neither is configurable here.
        /// </summary>
        public List<string> AcceptedBodyTypes { get; set; } = new();
    }
}
