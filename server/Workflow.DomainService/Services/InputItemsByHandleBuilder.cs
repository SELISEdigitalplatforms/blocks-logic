using Workflow.DomainService.Entities;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Services
{
    /// <summary>Groups a node's input items by the target handle of the edge they arrived on (used by Merge).</summary>
    public static class InputItemsByHandleBuilder
    {
        /// <param name="incomingEdges">The node's incoming edges, in snapshot order.</param>
        /// <param name="items">The node's resolved input items.</param>
        /// <param name="nodeExecutions">The run's node-execution rows, to order items by their row's run index.</param>
        public static IReadOnlyDictionary<string, IReadOnlyList<WorkflowItemExecutionEntity>> Build(
            IEnumerable<EdgeEnity>? incomingEdges,
            IReadOnlyList<WorkflowItemExecutionEntity>? items,
            IEnumerable<NodeExecutionEntity>? nodeExecutions)
        {
            var result = new Dictionary<string, List<WorkflowItemExecutionEntity>>(StringComparer.Ordinal);
            var all = items ?? Array.Empty<WorkflowItemExecutionEntity>();
            var runIndexByRow = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in nodeExecutions ?? Array.Empty<NodeExecutionEntity>())
            {
                if (row != null && !string.IsNullOrEmpty(row.Id)) runIndexByRow[row.Id] = row.RunIndex;
            }

            foreach (var edge in incomingEdges ?? Array.Empty<EdgeEnity>())
            {
                if (edge == null) continue;
                var key = string.IsNullOrWhiteSpace(edge.TargetHandle) ? string.Empty : edge.TargetHandle;
                var branch = WorkflowBranchRouting.BranchKey(edge.SourceHandle);
                var fromEdge = all
                    .Where(item => item.NodeId == edge.Source && item.Branch == branch)
                    .OrderBy(item => item.NodeExecutionId != null && runIndexByRow.TryGetValue(item.NodeExecutionId, out var run) ? run : int.MaxValue)
                    .ThenBy(item => item.ItemIndex);
                if (!result.TryGetValue(key, out var list))
                {
                    list = new List<WorkflowItemExecutionEntity>();
                    result[key] = list;
                }
                list.AddRange(fromEdge);
            }

            return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<WorkflowItemExecutionEntity>)pair.Value, StringComparer.Ordinal);
        }
    }
}
