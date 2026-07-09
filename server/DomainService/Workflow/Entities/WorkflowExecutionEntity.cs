using DomainService.Workflow.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;


namespace DomainService.Workflow.Entities
{
    [BsonIgnoreExtraElements]
    public class WorkflowExecutionEntity
    {
        public required string Id { get; set; }
        public required string TenantId { get; set; }
        public required string WorkflowId { get; set; }
        public required string WorkflowName { get; set; }
        public required WorkflowEntity WorkflowSnapshot { get; set; }
        public WorkflowExecutionStatus Status { get; set; } = WorkflowExecutionStatus.Init;
        public WorkflowExecutionMode ExecutionMode { get; set; } = WorkflowExecutionMode.Test;
        public List<string> ActiveNodeIds { get; set; } = new();
        public BsonDocument Context { get; set; } = new BsonDocument();
        public List<NodeExecutionEntity> NodeExecutions { get; set; } = new();
        public required TriggerMetadata TriggerMetadata { get; set; } = new();
        // Retry / hierarchy
        public string? OriginalExecutionId { get; set; }
        public int AttemptNumber { get; set; } = 1;
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class TriggerMetadata
    {
        public string? TriggerNodeId { get; set; }
        public string? TriggerType { get; set; }
        public BsonArray? TriggerData { get; set; }
    }
}