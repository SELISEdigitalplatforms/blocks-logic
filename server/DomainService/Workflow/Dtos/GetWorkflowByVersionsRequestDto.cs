using System.ComponentModel.DataAnnotations;

namespace DomainService.Workflow.Dtos
{
    public class GetWorkflowByVersionRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }

        [Required]
        public required string VersionId { get; set; }

    }

}