using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Workflow.Models
{
    [BsonIgnoreExtraElements]
    public class WorkflowModel : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public required string TenantId { get; set; }

        public List<NodeModel> Nodes { get; set; } = new();


        public List<EdgeModel> Edges { get; set; } = new();

        public Dictionary<string, string> Settings { get; set; }

        public bool IsPublished { get; set; } = false;

        public bool IsDirty { get; set; } = true;

        public string Description { get; set; } = string.Empty;

        public string? PublishedVersionId { get; set; }

        public Dictionary<string, List<NodeOutputSchemaField>>? NodeOutputSchemas { get; set; }

    }
}