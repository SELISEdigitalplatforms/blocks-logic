using System.ComponentModel.DataAnnotations;

namespace Workflow.DomainService.Dtos;

public class WorkflowVersionCreateRequestDto
{
    [Required]
    public required string WorkflowId { get; set; }

    [Required]
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

}