using System.ComponentModel.DataAnnotations;
using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowGetsRequestDto : IProjectKey
    {
        [Required]
        public required string ProjectKey { get; set; }
        public string? Search { get; set; }
        public bool? IsActive { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "PageSize must be greater than 0")]
        public int PageSize { get; set; } = 10;

        [Range(0, int.MaxValue, ErrorMessage = "PageNumber must be greater than or equal to 0")]
        public int PageNumber { get; set; } = 0;

    }

}
