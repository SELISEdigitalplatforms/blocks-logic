namespace Workflow.DomainService.Events
{

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record AddExcuationNodeEvent
    {
        /// <summary>
        /// Tenant the execution belongs to. New publishers set this on the payload; Genesis also
        /// carries it on the bus as ApplicationProperties / BlocksContext. Must not be <c>required</c>:
        /// System.Text.Json would then reject in-flight messages published before this field existed.
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        public required string WorkflowId { get; set; }
        public required string WorkflowExecutionId { get; set; }
        public required string NodeId { get; set; }
    }

}

