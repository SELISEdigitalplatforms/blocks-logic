using Blocks.Genesis;

namespace DomainService.Workflow.Models
{
    public class WorkflowModel : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public required string TenantId { get; set; }

        public List<NodeModel> Nodes { get; set; } = new();


        public List<EdgeModel> Edges { get; set; } = new();

        public Dictionary<string, string> Settings { get; set; }

        public bool IsActive { get; set; } = true;

        public string Description { get; set; } = string.Empty;

        public string? PublishedVersionId { get; set; }

        public Dictionary<string, List<NodeOutputSchemaField>>? NodeOutputSchemas { get; set; }

    }
}