using System.ComponentModel.DataAnnotations;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowUnpublishRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }

    }

}