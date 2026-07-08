using System.ComponentModel.DataAnnotations;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Dtos;

public class WorkflowVersionUpdateRequestDto
{
    [Required]
    public required string VersionId { get; set; }

    [Required]
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

}