namespace DomainService.Workflow.Nodes.TriggerWebhookV1
{
    public class TriggerWebhookV1Parameters
    {
        public string Path { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = "POST";
        public string HttpResponseMode { get; set; } = "immediate";
        public string AuthType { get; set; } = "none";
        public string HttpResponseData { get; set; } = "all";
    }
}
