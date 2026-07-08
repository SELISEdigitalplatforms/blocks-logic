namespace DomainService.Workflow.Dtos
{
    public class StepExecuteRequestDto
    {
        public required string WorkflowId { get; set; }
        public required string NodeId { get; set; }
        public string? SourceExecutionId { get; set; }
        public string? TriggerNodeId { get; set; }
    }
}