namespace DomainService.Workflow.Nodes
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
        Task<NodeExecutionResult> RunAsync(NodeExecutionContext context);
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

        public static NodeExecutionResult Failed(string error)
            => new() { IsSuccess = false, ErrorMessage = error };
    }
}
