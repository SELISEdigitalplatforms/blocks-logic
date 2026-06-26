

using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowGetsResponseDto : BaseQueryListResponse<List<WorkflowListItemDto>>
    {
    }

    public class WorkflowListItemDto
    {
        public required string ItemId { get; set; }
        public required string Name { get; set; }
        public required bool IsPublished { get; set; }
        public Dictionary<string, string> Settings { get; set; } = new();
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdatedDate { get; set; }

        public string? CreatedBy { get; set; }

        public string? LastUpdatedBy { get; set; }

        public string? Language { get; set; }

        public List<string> Tags { get; set; } = new();


    }
}