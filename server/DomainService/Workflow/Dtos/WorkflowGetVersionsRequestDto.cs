using System.ComponentModel.DataAnnotations;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowGetVersionsRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }

    }

}