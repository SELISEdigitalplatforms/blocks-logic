using System.Text.Json;

namespace DomainService.Workflow.Dtos
{
    /// <summary>
    /// Represents a single data item that flows through the workflow.
    /// Frontend will use this to build node input/output visualization.
    /// </summary>
    public class WorkflowItemExecutionDto
    {
        public required string ItemId { get; set; }
        public required string NodeId { get; set; }
        public required string NodeExecutionId { get; set; }
        public required string Branch { get; set; }
        public required JsonDocument Data { get; set; }
        public List<string> ParentItemIds { get; set; } = new();
        public int ItemIndex { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
