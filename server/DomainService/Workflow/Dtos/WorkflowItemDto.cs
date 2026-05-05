

using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowItemDto
    {
        public string Name { get; set; } = string.Empty;

        public Dictionary<string, string> Settings { get; set; } = new();

        public string ItemId { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; }

        public DateTime LastUpdatedDate { get; set; }

        public string? CreatedBy { get; set; }

        public string? LastUpdatedBy { get; set; }

        public string? Language { get; set; }

        public List<string> Tags { get; set; } = new();

        public bool IsActive { get; set; }
    }
}