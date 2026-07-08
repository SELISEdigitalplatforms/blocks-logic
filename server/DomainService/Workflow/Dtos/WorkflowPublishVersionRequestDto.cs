using System.ComponentModel.DataAnnotations;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowPublishVersionRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }

        public string? VersionId { get; set; }


    }

}