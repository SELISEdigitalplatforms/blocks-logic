namespace Workflow.DomainService.Nodes.TriggerBlocksEventV2
{
    /// <summary>
    /// Parameters for the Action trigger node.
    /// Stored in the workflow node's Parameters BsonDocument.
    /// </summary>
    public class TriggerBlocksEventV2Parameters
    {
        /// <summary>
        /// The action that starts the workflow (e.g. "iam.user.created"). The name is kept from the Blocks Event node.
        /// </summary>
        public string EventName { get; set; } = string.Empty;
    }
}
