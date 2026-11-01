using System.ComponentModel.DataAnnotations;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowGetsRequestDto
    {
        public string? Search { get; set; }
        public bool? IsPublished { get; set; }
        /// <summary>True: only published workflows that contain an Error Trigger (the alert's workflow picker).</summary>
        public bool? HasErrorTrigger { get; set; }
        /// <summary>Leaves this workflow out (the workflow whose settings are open).</summary>
        public string? ExcludeItemId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "PageSize must be greater than 0")]
        public int PageSize { get; set; } = 10;

        [Range(0, int.MaxValue, ErrorMessage = "PageNumber must be greater than or equal to 0")]
        public int PageNumber { get; set; } = 0;

    }

}
