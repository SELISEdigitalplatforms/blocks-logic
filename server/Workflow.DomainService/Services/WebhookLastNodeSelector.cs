using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// Picks the webhook response node after an in-process run has finished.
    /// The walk follows only edges whose branch actually carried items, then returns
    /// the reached terminal with the highest <see cref="NodeExecutionEntity.RunIndex"/>.
    /// </summary>
    public static class WebhookLastNodeSelector
    {
        public static NodeExecutionEntity? Select(
            string triggerNodeId,
            IReadOnlyList<NodeExecutionEntity> nodeExecutions,
            IReadOnlyList<EdgeEnity> edges)
        {
            if (nodeExecutions == null || nodeExecutions.Count == 0)
            {
                return null;
            }

            var latestByNode = nodeExecutions
                .Where(ne => ne.Status is NodeExecutionStatus.Completed or NodeExecutionStatus.Failed)
                .GroupBy(ne => ne.NodeId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(ne => ne.RunIndex).First());

            // A missing trigger row means the graph cannot be walked. Keep the previous
            // "highest run index" choice so a broken execution still returns a body.
            if (string.IsNullOrEmpty(triggerNodeId) || !latestByNode.ContainsKey(triggerNodeId))
            {
                return nodeExecutions.MaxBy(ne => ne.RunIndex);
            }

            var edgesBySource = (edges ?? Array.Empty<EdgeEnity>())
                .GroupBy(e => e.Source)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EdgeEnity>)g.ToList());

            var reachable = new HashSet<string>();
            var hasTraversedChild = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(triggerNodeId);

            while (queue.Count > 0)
            {
                var nodeId = queue.Dequeue();
                if (!reachable.Add(nodeId))
                {
                    continue;
                }

                if (!latestByNode.TryGetValue(nodeId, out var execution))
                {
                    continue;
                }

                // A failed node does not dispatch children. Stop here even if a later
                // row exists on an outgoing edge.
                if (execution.Status == NodeExecutionStatus.Failed)
                {
                    continue;
                }

                if (!edgesBySource.TryGetValue(nodeId, out var outgoing))
                {
                    continue;
                }

                foreach (var edge in outgoing)
                {
                    if (!BranchCarriedItems(execution, edge.SourceHandle))
                    {
                        continue;
                    }

                    if (!latestByNode.ContainsKey(edge.Target))
                    {
                        continue;
                    }

                    hasTraversedChild.Add(nodeId);
                    queue.Enqueue(edge.Target);
                }
            }

            return reachable
                .Where(id => latestByNode.ContainsKey(id) && !hasTraversedChild.Contains(id))
                .Select(id => latestByNode[id])
                .OrderByDescending(ne => ne.RunIndex)
                .FirstOrDefault();
        }

        /// <summary>
        /// Same branch key as <c>WorkflowEngineService.ResolveEdgeBranch</c>: a blank handle is
        /// the main output (<c>source</c>); otherwise the handle string is the branch name
        /// (<c>source</c>, <c>if-true</c>, <c>if-false</c>).
        /// </summary>
        private static string BranchKey(string? sourceHandle)
        {
            return string.IsNullOrWhiteSpace(sourceHandle) ? "source" : sourceHandle;
        }

        private static bool BranchCarriedItems(NodeExecutionEntity execution, string? sourceHandle)
        {
            var counts = execution.OutputCountsByBranch;
            if (counts == null)
            {
                return false;
            }

            return counts.TryGetValue(BranchKey(sourceHandle), out var count) && count > 0;
        }
    }
}
