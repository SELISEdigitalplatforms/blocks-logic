using System.ComponentModel.DataAnnotations;

namespace Workflow.DomainService.Dtos;

public class WorkflowVersionUpdateRequestDto
{
    [Required]
    public required string VersionId { get; set; }

    [Required]
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

}