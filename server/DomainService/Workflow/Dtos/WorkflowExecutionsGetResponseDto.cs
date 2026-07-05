using Blocks.Genesis;
using DomainService.Workflow.Enums;
using DomainService.Workflow.Models;

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
        public required WorkflowExecutionMode ExecutionMode { get; set; }
        public required DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public int AttemptNumber { get; set; }
        public TriggerMetadata? TriggerMetadata { get; set; } = new();
    }
}
