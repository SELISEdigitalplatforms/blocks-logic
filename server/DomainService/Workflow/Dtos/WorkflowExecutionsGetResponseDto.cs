using Blocks.Genesis;
using DomainService.Workflow.Enums;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowExecutionsGetResponseDto : BaseQueryListResponse<List<WorkflowExecutionItemDto>>
    {
    }

    public class WorkflowExecutionItemDto
    {
        public required string Id { get; set; }
        public required string WorkflowId { get; set; }
        public required string WorkflowName { get; set; }
        public required WorkflowExecutionStatus Status { get; set; }
        public required DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public required string TriggerType { get; set; }
        public int AttemptNumber { get; set; }
        public Dictionary<string, string> TriggerMetadata { get; set; } = new();
    }
}
