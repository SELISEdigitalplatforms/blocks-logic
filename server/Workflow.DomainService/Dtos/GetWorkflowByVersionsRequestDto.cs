using System.ComponentModel.DataAnnotations;

namespace Workflow.DomainService.Dtos
{
    public class GetWorkflowByVersionRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }

        [Required]
        public required string VersionId { get; set; }

    }

}