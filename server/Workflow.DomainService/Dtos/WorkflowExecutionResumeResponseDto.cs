namespace Workflow.DomainService.Dtos
{
    /// <summary>The answer to a resume: whether the failed execution was continued, and from which nodes.</summary>
    public class WorkflowExecutionResumeResponseDto
    {
        public bool IsSuccess { get; set; }
        public string ExecutionId { get; set; } = string.Empty;
        public List<string> ResumedNodeIds { get; set; } = [];
        public string? Error { get; set; }
    }
}
