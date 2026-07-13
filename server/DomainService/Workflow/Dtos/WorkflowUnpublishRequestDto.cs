using System.ComponentModel.DataAnnotations;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowUnpublishRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }

    }

}