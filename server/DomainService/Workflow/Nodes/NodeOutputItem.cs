using System.Reflection.Metadata;
using MongoDB.Bson;

namespace DomainService.Workflow.Nodes
{
    public class NodeOutputItem
    {
        public required NodeOutputItemData Data { get; set; }
        public required string Branch { get; set; }
        public List<string>? ParentItemIds { get; set; }


    }
    public class NodeOutputItemData
    {
        public BsonValue Parameters { get; set; } = new BsonDocument();
        public BsonValue Input { get; set; } = new BsonDocument();
        public BsonValue Output { get; set; } = new BsonDocument();

    }

}
