

using Blocks.Genesis;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowGetVersionsResponseDto : BaseQueryListResponse<List<WorkflowGetVersionSummary>>
    {
    }

    public class WorkflowGetVersionSummary
    {
        public required string ItemId { get; set; }
        public required string WorkflowId { get; set; }
        public required string TenantId { get; set; }
        public required string Name { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdatedDate { get; set; }
        public string? CreatedBy { get; set; }
        public string? LastUpdatedBy { get; set; }
    }

}