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

        public bool IsDirty { get; set; } = true;

        public bool IsPublished { get; set; } = false;

        public string? PublishedVersionId { get; set; }

        public string? LastPublishedVersionId { get; set; }

        public PublishedWorkflowMeta? PublishedMeta { get; set; }
        public TestWorkflowMeta? TestMeta { get; set; }

        public string Description { get; set; } = string.Empty;

        public Dictionary<string, List<NodeOutputSchemaField>>? NodeOutputSchemas { get; set; }


    }

    public class PublishedWorkflowMeta
    {
        public List<NodeModel> TriggerNodes { get; set; } = new();
    }

    public class TestWorkflowMeta
    {
        public List<NodeModel> ListenerTriggerNodes { get; set; } = new();
        public List<string> UserIds { get; set; } = new();
        public bool IsListening { get; set; } = false;
        public string? CompletionNodeId { get; set; }
    }
}