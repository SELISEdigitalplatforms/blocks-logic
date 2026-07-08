using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Dtos;

public class WorkflowCreateRequestDto
{
    [Required]
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    public JsonElement Nodes { get; set; } = JsonDocument.Parse("[]").RootElement;

    public List<EdgeModel> Edges { get; set; } = new();

    public Dictionary<string, string> Settings { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Dictionary<string, List<NodeOutputSchemaField>> NodeOutputSchemas { get; set; } = new();
}