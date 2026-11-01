using Blocks.Genesis.Entities.Logic;
using MongoDB.Bson;
using Workflow.DomainService.Events;

namespace Workflow.DomainService.Nodes.TriggerErrorTriggerV2
{
    /// <summary>The item an Error Trigger outputs: the failed run and its error.</summary>
    public static class ErrorTriggerPayload
    {
        public static BsonDocument Build(ErrorTriggerEvent failure, WorkflowExecutionEntity failedExecution) =>
            Build(failure, failedExecution.WorkflowName, failedExecution.ExecutionMode, failedExecution.StartedAt, failedExecution.FinishedAt);

        private static BsonDocument Build(ErrorTriggerEvent failure, string workflowName, WorkflowExecutionMode mode, DateTime startedAt, DateTime? finishedAt) =>
            new()
            {
                {
                    "execution", new BsonDocument
                    {
                        { "workflowId", failure.FailedWorkflowId },
                        { "workflowName", workflowName ?? string.Empty },
                        { "executionId", failure.FailedExecutionId },
                        { "mode", mode.ToString() },
                        { "startedAt", startedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") },
                        { "finishedAt", finishedAt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") ?? string.Empty },
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
            };

        /// <summary>What a step test of the Error Trigger outputs, so the next nodes can be built without a real failure.</summary>
        public static BsonDocument Sample() => Build(
            new ErrorTriggerEvent
            {
                FailedWorkflowId = "wf_sample",
                FailedExecutionId = "exec_sample_01",
                Error = new ErrorTriggerEventError
                {
                    Message = "Example error message",
                    NodeId = "node_sample",
                    NodeName = "Node with error",
                    NodeType = "httpRequest",
                },
            },
            "Example workflow",
            WorkflowExecutionMode.Production,
            new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 9, 0, 3, DateTimeKind.Utc));
    }
}
