using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Events
{
    /// <summary>
    /// Sent once per failed attempt of a production run whose workflow has an enabled alert.
    /// The Worker's ErrorTriggerConsumer starts the alert's error workflow from it.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record ErrorTriggerEvent
    {
        public string TenantId { get; set; } = string.Empty;
        public string FailedWorkflowId { get; set; } = string.Empty;
        public string FailedExecutionId { get; set; } = string.Empty;
        public int AttemptNumber { get; set; } = 1;
        public WorkflowExecutionMode ExecutionMode { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public ErrorTriggerEventError Error { get; set; } = new();
        /// <summary>Copied from the failed workflow's settings when the run failed.</summary>
        public ErrorTriggerEventAlert Alert { get; set; } = new();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record ErrorTriggerEventError
    {
        public string Message { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public string NodeType { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record ErrorTriggerEventAlert
    {
        public WorkflowAlertMode Mode { get; set; } = WorkflowAlertMode.Workflow;
        public string? WorkflowId { get; set; }
    }
}
