using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Workflow.Models
{
    [BsonIgnoreExtraElements]
    public class WorkflowVersionModel : BaseEntity
    {
        public required string WorkflowId { get; set; }
        public required string TenantId { get; set; }
        public required string Name { get; set; }
        public string Description { get; set; } = string.Empty;
        public required WorkflowModel Snapshot { get; set; }
    }
}