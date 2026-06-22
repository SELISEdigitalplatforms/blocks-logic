using Blocks.Genesis;

namespace DomainService.Workflow.Models
{
    public class WorkflowSnapshotModel : BaseEntity
    {
        public required string WorkflowId { get; set; }
        public required string TenantId { get; set; }
        public required string? Name { get; set; }
        public string? Description { get; set; }
        public required string Snapshot { get; set; }
        public required string SnapshotHash { get; set; }
    }
}