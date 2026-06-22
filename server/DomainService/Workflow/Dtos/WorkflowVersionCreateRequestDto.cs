
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blocks.Genesis;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Dtos;

public class WorkflowVersionCreateRequestDto : IProjectKey
{
    [Required]
    public required string ProjectKey { get; set; }

    [Required]
    public required string WorkflowId { get; set; }

    [Required]
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

}
