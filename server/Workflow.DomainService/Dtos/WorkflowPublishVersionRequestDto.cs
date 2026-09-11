using System.ComponentModel.DataAnnotations;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowPublishVersionRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }

        public string? VersionId { get; set; }


    }

}