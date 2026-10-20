namespace Workflow.DomainService.Nodes
{
    /// <summary>
    /// Picks the executor for a node by (type, version), so a later node version can change behaviour
    /// without touching the earlier one. A version the type does not declare falls back to "v1"
    /// (node versions follow v1, v2, v3 …; anything else, e.g. "1.0", is treated as v1).
    /// </summary>
    public sealed class NodeExecutorRegistry
    {
        public const string FallbackVersion = "v1";

        /// <summary>Stage line for a fallback. Fixed text: never the node type, version strings or values.</summary>
        public const string FallbackLogMessage = "Node version is not registered for this node type; running version v1.";

        private readonly Dictionary<(string Type, string Version), INodeExecutor> _executors = new();

        public NodeExecutorRegistry(IEnumerable<INodeExecutor> executors)
        {
            ArgumentNullException.ThrowIfNull(executors);
            foreach (var executor in executors)
            {
                foreach (var version in executor.Versions)
                {
                    if (!_executors.TryAdd((executor.NodeType, version), executor))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate node executor for type '{executor.NodeType}' version '{version}'.");
                    }
                }
            }
        }

        /// <summary>
        /// The executor for (type, version), else (type, "v1") with <see cref="NodeExecutorResolution.UsedFallback"/> set.
        /// An unknown type throws, so the step fails through the engine's normal failure path.
        /// </summary>
        public NodeExecutorResolution Resolve(string? type, string? version)
        {
            var nodeType = type ?? string.Empty;
            if (_executors.TryGetValue((nodeType, version ?? string.Empty), out var executor))
            {
                return new NodeExecutorResolution(executor, UsedFallback: false);
            }

            if (_executors.TryGetValue((nodeType, FallbackVersion), out var fallback))
            {
                return new NodeExecutorResolution(fallback, UsedFallback: true);
            }

            throw new InvalidOperationException("No executor for node type.");
        }
    }

    public readonly record struct NodeExecutorResolution(INodeExecutor Executor, bool UsedFallback);
}
