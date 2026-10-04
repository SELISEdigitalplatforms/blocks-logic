using System.ComponentModel.DataAnnotations;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowGetRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }
    }

}