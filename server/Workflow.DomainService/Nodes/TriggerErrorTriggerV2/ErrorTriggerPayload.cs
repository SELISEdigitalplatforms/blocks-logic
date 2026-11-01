using MongoDB.Bson;

namespace Workflow.DomainService.Nodes.TriggerErrorTriggerV2
{
    /// <summary>The item an Error Trigger outputs: the failed run and its workflow.</summary>
    public static class ErrorTriggerPayload
    {
        public static BsonDocument Build(
            string executionId, int attemptNumber, string mode, DateTime startedAt, DateTime? finishedAt,
            string errorMessage, string nodeId, string nodeName, string nodeType,
            string workflowId, string workflowName) =>
            new()
            {
                {
                    "execution", new BsonDocument
                    {
                        { "id", executionId },
                        { "attemptNumber", attemptNumber },
                        { "mode", mode },
                        { "startedAt", startedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") },
                        { "finishedAt", finishedAt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") ?? string.Empty },
                        {
                            "error", new BsonDocument
                            {
                                { "message", errorMessage },
                                { "nodeId", nodeId },
                                { "nodeName", nodeName },
                                { "nodeType", nodeType },
                            }
                        },
                    }
                },
                { "workflow", new BsonDocument { { "id", workflowId }, { "name", workflowName } } },
            };

        /// <summary>What a step test of the Error Trigger outputs, so the next nodes can be built without a real failure.</summary>
        public static BsonDocument Sample() => Build(
            "exec_sample_01", 1, "Production",
            new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 9, 0, 3, DateTimeKind.Utc),
            "Example error message", "node_sample", "Node with error", "httpRequest",
            "wf_sample", "Example workflow");
    }
}
