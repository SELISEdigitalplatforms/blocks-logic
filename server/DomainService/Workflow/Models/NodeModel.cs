using MongoDB.Bson;

namespace DomainService.Workflow.Models;

public class NodeModel
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string Type { get; set; }
    public required string Version { get; set; }
    public required Position Position { get; set; }
    public BsonDocument Parameters { get; set; } = new BsonDocument();
    public BsonDocument Settings { get; set; } = new BsonDocument();
    public BsonArray? PinData { get; set; }
}

public class Position
{
    public double X { get; set; }
    public double Y { get; set; }
}