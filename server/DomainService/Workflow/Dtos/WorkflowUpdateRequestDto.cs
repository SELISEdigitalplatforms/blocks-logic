using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Dtos;

public class WorkflowUpdateRequestDto
{
    [Required]
    public required string ItemId { get; set; }
    public string? Name { get; set; }
    public List<NodeDto>? Nodes { get; set; }
    public List<EdgeModel>? Edges { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
    public bool? IsPublished { get; set; }
}


public class NodeDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string Type { get; set; }
    public required string Version { get; set; }
    public required Position Position { get; set; }
    public Handle? Handle { get; set; }
    public JsonElement Parameters { get; set; } = new();
    public JsonElement Settings { get; set; } = new();
    public JsonElement? PinData { get; set; }

}