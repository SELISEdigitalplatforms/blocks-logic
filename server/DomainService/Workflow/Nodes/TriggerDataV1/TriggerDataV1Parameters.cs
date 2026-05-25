namespace DomainService.Workflow.Nodes.TriggerDataV1
{
    /// <summary>
    /// Parameters for the Data Trigger node.
    /// Stored in the workflow node's Parameters BsonDocument.
    /// </summary>
    public class TriggerDataV1Parameters
    {
        /// <summary>
        /// The collection/schema name to monitor (e.g., "Orders")
        /// </summary>
        public string CollectionName { get; set; } = string.Empty;

        /// <summary>
        /// The schema display name
        /// </summary>
        public string SchemaName { get; set; } = string.Empty;

        /// <summary>
        /// The operation to monitor: "Inserted", "Updated", or "Deleted"
        /// </summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// The project/tenant key
        /// </summary>
        public string ProjectKey { get; set; } = string.Empty;
    }
}
