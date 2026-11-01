namespace Workflow.DomainService.Events
{
    /// <summary>
    /// Sent once per failed attempt of a production run, or of a full test run ("Execute workflow"),
    /// whose workflow has an enabled alert.
    /// The Worker's ErrorTriggerConsumer reads the failed run and starts its alert's error workflow.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record ErrorTriggerEvent
    {
        public string TenantId { get; set; } = string.Empty;
        public string FailedWorkflowId { get; set; } = string.Empty;
        public string FailedExecutionId { get; set; } = string.Empty;
        public ErrorTriggerEventError Error { get; set; } = new();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record ErrorTriggerEventError
    {
        public string Message { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public string NodeType { get; set; } = string.Empty;
    }
}
