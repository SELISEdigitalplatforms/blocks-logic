namespace Workflow.DomainService.Nodes.TriggerBlocksEventV2
{
    /// <summary>
    /// Parameters for the Blocks Event trigger node.
    /// Stored in the workflow node's Parameters BsonDocument.
    /// </summary>
    public class TriggerBlocksEventV2Parameters
    {
        /// <summary>
        /// The catalog event that starts the workflow (e.g. "UserMutationEvent")
        /// </summary>
        public string EventName { get; set; } = string.Empty;
    }
}
