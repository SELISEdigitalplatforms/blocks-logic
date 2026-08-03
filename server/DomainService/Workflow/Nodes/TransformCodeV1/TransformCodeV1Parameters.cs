namespace DomainService.Workflow.Nodes.TransformCodeV1
{
    public class TransformCodeV1Parameters
    {
        public string Mode { get; set; } = "all";
        public string Language { get; set; } = "js";
        public string Script { get; set; } = string.Empty;
    }
}