using System.ComponentModel.DataAnnotations;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowGetVersionsRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }

    }

}