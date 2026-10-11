using System.Reflection.Metadata;
using MongoDB.Bson;

namespace Workflow.DomainService.Nodes
{
    public class NodeOutputItem
    {
        public required NodeOutputItemData Data { get; set; }
        public required string Branch { get; set; }
        public List<string>? ParentItemIds { get; set; }

        /// <summary>
        /// Built by the node to report a failed item (<c>TryBuildErrorOutputItem</c>), not data an upstream
        /// returned. In memory only: a step with any such item fails, so the run ends Failed (C-9, 2026-10-08).
        /// Not inferred from the data, since a normal API answer can itself contain <c>"error": true</c>.
        /// </summary>
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public bool IsError { get; init; }


    }
    public class NodeOutputItemData
    {
        public BsonValue Parameters { get; set; } = new BsonDocument();
        public BsonValue Input { get; set; } = new BsonDocument();
        public BsonValue Output { get; set; } = new BsonDocument();

    }

}
