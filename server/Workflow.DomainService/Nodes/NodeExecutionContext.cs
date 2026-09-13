using MongoDB.Bson;
using Workflow.DomainService.Entities;

namespace Workflow.DomainService.Nodes
{
    /// <summary>
    /// Runtime-only context passed to node executors during execution.
    /// This context is never persisted and exists only during node execution.
    /// Provides nodes with access to workflow state and execution environment.
    /// </summary>
    public class NodeExecutionContext
    {
        public required string WorkflowExecutionId { get; set; }

        /// <summary>The workflow definition this run belongs to. Carried so a node can attribute an
        /// outbound side effect to the workflow, not just to the run.</summary>
        public string WorkflowId { get; set; } = string.Empty;

        /// <summary>Id of the node being executed, for the same reason.</summary>
        public string NodeId { get; set; } = string.Empty;
        public required string TenantId { get; set; }
        public required BsonDocument Parameters { get; set; }
        public required IReadOnlyList<WorkflowItemExecutionEntity> InputItems { get; set; }
        public required int IterationCount { get; init; }

        /// <summary>
        /// Whether any edge targets this node. This is the only way a node can tell "nothing is wired to me"
        /// from "my upstream ran and produced no items" — both arrive as an empty <see cref="InputItems"/>,
        /// but they mean opposite things. The second case is how an untaken branch is pruned (the engine
        /// dispatches to every outgoing edge and lets the zero-item node do nothing), so a node must never
        /// act on an empty input unless this is <c>false</c>.
        /// </summary>
        public bool HasUpstream { get; init; }
        public required BsonDocument WorkflowContext { get; init; }
        public IReadOnlyDictionary<string, List<WorkflowItemExecutionEntity>> AncestorNodeOutputs { get; set; } = new Dictionary<string, List<WorkflowItemExecutionEntity>>();
        public bool IsRetry { get; set; }
        public int AttemptNumber { get; set; } = 1;
        public CancellationToken CancellationToken { get; set; } = default;
        public IServiceProvider? ServiceProvider { get; set; }
    }
}
