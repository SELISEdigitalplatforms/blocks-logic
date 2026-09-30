using System.Text.Json.Serialization;
using Blocks.Genesis;
using Workflow.DomainService.Enums;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowExecutionsGetResponseDto : BaseQueryListResponse<List<WorkflowExecutionItemDto>>
    {
        /// <summary>
        /// Status patches for ids the client already rendered. Not part of <see cref="BaseQueryResponse{T}.Data"/>.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<WorkflowExecutionItemDto>? Refreshed { get; set; }

        /// <summary>HTTP status the controller returns. Omitted from the JSON body.</summary>
        [JsonIgnore]
        public int HttpStatus { get; set; } = 200;
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
    }
}
