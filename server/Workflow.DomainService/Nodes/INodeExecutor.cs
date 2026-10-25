namespace Workflow.DomainService.Nodes
{
    /// <summary>
    /// Interface that all workflow node executors must implement.
    /// Nodes are stateless and data-flow oriented, receiving context and returning output items.
    /// The workflow engine handles all state management, lineage tracking, and persistence.
    /// </summary>
    public interface INodeExecutor
    {
        string NodeType { get; }
        string Version { get; }

        /// <summary>
        /// Node versions this executor runs; each is a <see cref="NodeExecutorRegistry"/> key. Existing
        /// executors run "v1" and "v2" alike ("v2" only means the node owns its handle).
        /// </summary>
        IReadOnlyCollection<string> Versions => DefaultVersions;

        Task<NodeExecutionResult> RunAsync(NodeExecutionContext context);

        private static readonly IReadOnlyCollection<string> DefaultVersions = new[] { "v1", "v2" };
    }




    public class NodeExecutionResult
    {
        public bool IsSuccess { get; set; } = true;
        public List<NodeOutputItem> OutputItems { get; set; } = new();
        public string? ErrorMessage { get; set; }

        public Dictionary<string, string>? ContextUpdates { get; set; }

        public static NodeExecutionResult Empty() => new();

        public static NodeExecutionResult Successful(List<NodeOutputItem> items)
            => new() { IsSuccess = true, OutputItems = items };

        public static NodeExecutionResult Failed(string error, List<NodeOutputItem>? outputItems = null)
            => new() { IsSuccess = false, ErrorMessage = error, OutputItems = outputItems ?? new() };
    }
}
