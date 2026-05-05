namespace DomainService.Workflow.Models;

public class EdgeModel
{
    public required string Id { get; set; }
    public required string Source { get; set; }
    public required string Target { get; set; }
    public required string SourceHandle { get; set; }
    public required string TargetHandle { get; set; }
}