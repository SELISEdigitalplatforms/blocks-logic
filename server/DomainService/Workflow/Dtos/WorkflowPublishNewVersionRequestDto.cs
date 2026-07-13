using System.ComponentModel.DataAnnotations;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowPublishNewVersionRequestDto
    {
        [Required]
        public required string WorkflowId { get; set; }

        [Required]
        public required string Name { get; set; }

        public string Description { get; set; } = string.Empty;

    }

}