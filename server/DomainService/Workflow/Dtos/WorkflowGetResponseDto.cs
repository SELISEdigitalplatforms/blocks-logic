using Blocks.Genesis;
using DomainService.Workflow.Models;
namespace DomainService.Workflow.Dtos
{
    public class WorkflowGetResponseDto : BaseResponse
    {
        public Workflow data { get; set; }
    }

    public class Workflow : BaseEntity
    {
        public string Name { get; set; }

        public required string TenantId { get; set; }

        public List<NodeDto> Nodes { get; set; } = new();


        public List<EdgeModel> Edges { get; set; } = new();

        public Dictionary<string, string> Settings { get; set; } = new();

        public bool IsActive { get; set; } = true;

        public string Description { get; set; } = string.Empty;

        public Dictionary<string, List<NodeOutputSchemaField>>? NodeOutputSchemas { get; set; }
    }


}