using Blocks.Genesis;
using DomainService.Workflow.Models;
namespace DomainService.Workflow.Dtos
{
    public class WorkflowGetResponseDto : BaseResponse
    {
        public WorkflowResponseDto data { get; set; }
    }

    public class WorkflowResponseDto : BaseEntity
    {
        public string Name { get; set; }

        public required string TenantId { get; set; }

        public List<NodeDto> Nodes { get; set; } = new();

        public List<EdgeModel> Edges { get; set; } = new();

        public Dictionary<string, string> Settings { get; set; } = new();

        public bool IsActive { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? PublishedVersionId { get; set; }

        public bool HasUnpublishedChanges { get; set; }

        public Dictionary<string, List<NodeOutputSchemaField>>? NodeOutputSchemas { get; set; }
    }

}