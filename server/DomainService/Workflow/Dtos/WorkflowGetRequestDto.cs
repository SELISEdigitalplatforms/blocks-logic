using System.ComponentModel.DataAnnotations;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowGetRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }
    }

}