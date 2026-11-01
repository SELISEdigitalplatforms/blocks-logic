using Blocks.Genesis.Entities.Logic;
using MongoDB.Bson;
using Workflow.DomainService.Events;

namespace Workflow.DomainService.Nodes.TriggerErrorTriggerV2
{
    /// <summary>The item an Error Trigger outputs: the failed run and its workflow.</summary>
    public static class ErrorTriggerPayload
    {
        public static BsonDocument Build(ErrorTriggerEvent failure, string workflowName) =>
            new()
            {
                {
                    "execution", new BsonDocument
                    {
                        { "id", failure.FailedExecutionId },
                        { "attemptNumber", failure.AttemptNumber },
                        { "mode", failure.ExecutionMode.ToString() },
                        { "startedAt", failure.StartedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") },
                        { "finishedAt", failure.FinishedAt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") ?? string.Empty },
                        {
                            "error", new BsonDocument
                            {
                                { "message", failure.Error.Message },
                                { "nodeId", failure.Error.NodeId },
                                { "nodeName", failure.Error.NodeName },
                                { "nodeType", failure.Error.NodeType },
                            }
                        },
                    }
                },
                { "workflow", new BsonDocument { { "id", failure.FailedWorkflowId }, { "name", workflowName } } },
            };

        /// <summary>What a step test of the Error Trigger outputs, so the next nodes can be built without a real failure.</summary>
        public static BsonDocument Sample() => Build(
            new ErrorTriggerEvent
            {
                FailedWorkflowId = "wf_sample",
                FailedExecutionId = "exec_sample_01",
                AttemptNumber = 1,
                ExecutionMode = WorkflowExecutionMode.Production,
                StartedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                FinishedAt = new DateTime(2026, 1, 1, 9, 0, 3, DateTimeKind.Utc),
                Error = new ErrorTriggerEventError
                {
                    Message = "Example error message",
                    NodeId = "node_sample",
                    NodeName = "Node with error",
                    NodeType = "httpRequest",
                },
            },
            "Example workflow");
    }
}
