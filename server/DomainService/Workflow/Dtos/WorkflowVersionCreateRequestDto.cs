using System.ComponentModel.DataAnnotations;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Dtos;

public class WorkflowVersionCreateRequestDto
{
    [Required]
    public required string WorkflowId { get; set; }

    [Required]
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

}