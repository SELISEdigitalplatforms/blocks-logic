using System.ComponentModel.DataAnnotations;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowDeleteRequestDto
    {
        [Required]
        public required string Id { get; set; }
    }

}