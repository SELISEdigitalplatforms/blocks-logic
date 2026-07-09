using System.ComponentModel.DataAnnotations;

namespace DomainService.Workflow.Dtos;

public class WorkflowVersionCreateRequestDto
{
    [Required]
    public required string WorkflowId { get; set; }

    [Required]
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

}