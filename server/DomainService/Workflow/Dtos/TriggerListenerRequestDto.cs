namespace DomainService.Workflow.Dtos
{
    public class TriggerListenerRequestDto
    {
        public required string WorkflowId { get; set; }
        public string? TriggerId { get; set; }
        public string? CompletionNodeId { get; set; }
        public bool EnableListener { get; set; } = false;

    }
}