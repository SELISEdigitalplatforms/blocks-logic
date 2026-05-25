using MongoDB.Bson;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Nodes
{
    /// <summary>
    /// Runtime-only context passed to node executors during execution.
    /// This context is never persisted and exists only during node execution.
    /// Provides nodes with access to workflow state and execution environment.
    /// </summary>
    public class NodeExecutionContext
    {
        public required string WorkflowExecutionId { get; set; }
        public required string TenantId { get; set; }
        public required BsonDocument Parameters { get; set; }
        public required IReadOnlyList<WorkflowItemExecutionModel> InputItems { get; set; }
        public required int IterationCount { get; init; }
        public required BsonDocument WorkflowContext { get; init; }
        public IReadOnlyDictionary<string, List<WorkflowItemExecutionModel>> AncestorNodeOutputs { get; set; } = new Dictionary<string, List<WorkflowItemExecutionModel>>();
        public bool IsRetry { get; set; }
        public int AttemptNumber { get; set; } = 1;
        public CancellationToken CancellationToken { get; set; } = default;
        public IServiceProvider? ServiceProvider { get; set; }
    }
}
