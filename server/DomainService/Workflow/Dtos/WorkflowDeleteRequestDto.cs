using System.ComponentModel.DataAnnotations;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowDeleteRequestDto
    {
        [Required]
        public required string Id { get; set; }
    }

}