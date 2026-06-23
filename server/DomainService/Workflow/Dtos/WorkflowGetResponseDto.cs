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

        public bool IsPublished { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? PublishedVersionId { get; set; }

        public WorkflowVersionDto? PublishedVersion { get; set; }

        public bool IsDirty { get; set; }

        public Dictionary<string, List<NodeOutputSchemaField>>? NodeOutputSchemas { get; set; }
    }

    public class WorkflowVersionDto
    {
        public string VersionId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

    }

}