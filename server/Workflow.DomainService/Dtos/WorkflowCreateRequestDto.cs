using System.ComponentModel.DataAnnotations;
using Workflow.DomainService.Entities;

namespace Workflow.DomainService.Dtos;

public class WorkflowCreateRequestDto
{
    [Required]
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    // Same node shape that Get returns and Update accepts. Nullable so an explicit
    // "nodes": null is treated as empty rather than failing implicit [Required] validation.
    public List<NodeDto>? Nodes { get; set; } = new();

    public List<EdgeEnity> Edges { get; set; } = new();

    public Dictionary<string, string> Settings { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

}
