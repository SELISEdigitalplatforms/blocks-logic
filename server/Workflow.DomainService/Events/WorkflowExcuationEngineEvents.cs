namespace Workflow.DomainService.Events
{

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record AddExcuationNodeEvent
    {
        public required string TenantId { get; set; }
        public required string WorkflowId { get; set; }
        public required string WorkflowExecutionId { get; set; }
        public required string NodeId { get; set; }
    }

}

