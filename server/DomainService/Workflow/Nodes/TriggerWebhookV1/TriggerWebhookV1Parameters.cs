using DomainService.Workflow.Services;

namespace DomainService.Workflow.Nodes.TriggerWebhookV1
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
    }
}
