using System.ComponentModel.DataAnnotations;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowGetsRequestDto
    {
        public string? Search { get; set; }
        public bool? IsPublished { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "PageSize must be greater than 0")]
        public int PageSize { get; set; } = 10;

        [Range(0, int.MaxValue, ErrorMessage = "PageNumber must be greater than or equal to 0")]
        public int PageNumber { get; set; } = 0;

    }

}
