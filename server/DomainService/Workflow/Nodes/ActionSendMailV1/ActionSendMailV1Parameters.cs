using System.Text.Json;

namespace DomainService.Workflow.Nodes.ActionSendMailV1
{
    public class ActionSendMailV1Parameters
    {
        public string ProjectKey { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public string Language { get; set; } = "en-US";
        public string To { get; set; } = string.Empty;
        public Dictionary<string, string> BodyDataContext { get; set; } = new Dictionary<string, string>();
    }
}
