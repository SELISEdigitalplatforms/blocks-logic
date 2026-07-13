using DomainService.Workflow.Nodes;

namespace DomainService.Workflow.Entities
{
    public class WorkflowItemExecutionEntity
    {
        public required string Id { get; set; }
        public required string WorkflowExecutionId { get; set; }
        public required string TenantId { get; set; }
        public required string NodeId { get; set; }
        public required string NodeExecutionId { get; set; }
        public required string NodeName { get; set; }
        public List<string> ParentItemIds { get; set; } = new();
        public Dictionary<string, string> AncestorMap { get; set; } = new();
        public required string Branch { get; set; }
        public required NodeOutputItemData Data { get; set; }
        public int ItemIndex { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
