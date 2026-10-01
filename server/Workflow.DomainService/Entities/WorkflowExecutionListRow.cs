using MongoDB.Bson.Serialization.Attributes;
using Workflow.DomainService.Enums;

namespace Workflow.DomainService.Entities
{
    /// <summary>
    /// List projection of <see cref="WorkflowExecutionEntity"/>. Heavy fields
    /// (snapshot, context, node executions) are not loaded.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class WorkflowExecutionListRow
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;

        public string WorkflowId { get; set; } = string.Empty;

        public string WorkflowName { get; set; } = string.Empty;

        public WorkflowExecutionStatus Status { get; set; }

        public WorkflowExecutionMode ExecutionMode { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? FinishedAt { get; set; }

        public string? ErrorMessage { get; set; }

        public int AttemptNumber { get; set; }
    }
}
