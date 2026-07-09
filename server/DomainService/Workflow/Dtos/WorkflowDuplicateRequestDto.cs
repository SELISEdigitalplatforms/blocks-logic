using System.ComponentModel.DataAnnotations;


namespace DomainService.Workflow.Dtos;

public class WorkflowDuplicateRequestDto
{
    [Required]
    public required string Name { get; set; }

    [Required]
    public required string WorkflowId { get; set; } = string.Empty;

}