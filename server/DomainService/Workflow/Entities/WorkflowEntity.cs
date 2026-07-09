using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Workflow.Entities
{
    [BsonIgnoreExtraElements]
    public class WorkflowEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public required string TenantId { get; set; }

        public List<NodeEntity> Nodes { get; set; } = new();

        public List<EdgeEnity> Edges { get; set; } = new();

        public Dictionary<string, string> Settings { get; set; }

        public bool IsDirty { get; set; } = true;

        public bool IsPublished { get; set; } = false;

        public string? PublishedVersionId { get; set; }

        public string? LastPublishedVersionId { get; set; }

        public PublishedWorkflowMeta? PublishedMeta { get; set; }
        public TestWorkflowMeta? TestMeta { get; set; }

        public string Description { get; set; } = string.Empty;

    }

    public class PublishedWorkflowMeta
    {
        public List<NodeEntity> TriggerNodes { get; set; } = new();
    }

    public class TestWorkflowMeta
    {
        public List<NodeEntity> ListenerTriggerNodes { get; set; } = new();
        public List<string> UserIds { get; set; } = new();
        public bool IsListening { get; set; } = false;
        public string? CompletionNodeId { get; set; }
    }
}