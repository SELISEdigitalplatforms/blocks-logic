using System.Text.Json;
using System.Text.Json.Nodes;
using DomainService.Workflow.Enums;
using Blocks.Genesis;
using DomainService.Workflow.Entities;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowExecutionGetResponseDto : BaseResponse
    {
        public WorkflowExecutionDto? Data { get; set; }
    }

    public class WorkflowExecutionDto
    {
        public required string Id { get; set; }
        public required string WorkflowId { get; set; }
        public required string WorkflowName { get; set; }
        public required WorkflowExecutionStatus Status { get; set; }
        public required WorkflowExecutionMode ExecutionMode { get; set; }
        public required DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public TimeSpan? Duration { get; set; }
        public string? ErrorMessage { get; set; }
        public int AttemptNumber { get; set; }
        public JsonElement Context { get; set; } = new();
        public List<string> ActiveNodeIds { get; set; } = new();
        public List<NodeExecutionResponseDto> NodeExecutions { get; set; } = new();
        public WorkflowResponseDto? WorkflowSnapshot { get; set; }
        public List<WorkflowItemExecutionDto> Items { get; set; } = new();
    }

    public class NodeExecutionResponseDto : NodeExecutionEntity
    {
        public JsonDocument Parameters { get; set; } = JsonDocument.Parse("{}");
        public JsonArray Input { get; set; } = new JsonArray();
        public JsonArray Output { get; set; } = new JsonArray();
    }
}
