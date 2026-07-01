namespace DomainService.Workflow.Dtos
{
    public class StepExecuteRequestDto
    {
        public required string ProjectKey { get; set; }
        public required string WorkflowId { get; set; }
        public required string NodeId { get; set; }
        public string? SourceExecutionId { get; set; }
    }
}