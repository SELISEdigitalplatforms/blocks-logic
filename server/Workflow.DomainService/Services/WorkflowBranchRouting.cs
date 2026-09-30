using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// Decides which outgoing edges carried items, and whether a node should wait for a parent
    /// that might still run. Branch keys match <see cref="WebhookLastNodeSelector"/>.
    /// </summary>
    public static class WorkflowBranchRouting
    {
        /// <summary>
        /// A blank handle is the main output (<c>source</c>). Otherwise the handle string is the
        /// branch name (<c>source</c>, <c>if-true</c>, <c>if-false</c>).
        /// </summary>
        public static string BranchKey(string? sourceHandle)
        {
            return string.IsNullOrWhiteSpace(sourceHandle) ? "source" : sourceHandle;
        }

        public static bool CarriedItems(IReadOnlyDictionary<string, int>? outputCountsByBranch, string? sourceHandle)
        {
            if (outputCountsByBranch == null)
            {
                return false;
            }

            return outputCountsByBranch.TryGetValue(BranchKey(sourceHandle), out var count) && count > 0;
        }

        /// <summary>
        /// Distinct targets of <paramref name="outgoingEdges"/> whose branch count is greater than 0.
        /// A mixed If batch returns both the true and false targets. A count of 0 drops only that edge.
        /// </summary>
        public static List<string> TakenTargets(
            IEnumerable<EdgeEnity>? outgoingEdges,
            IReadOnlyDictionary<string, int>? outputCountsByBranch)
        {
            var targets = new List<string>();
            if (outgoingEdges == null)
            {
                return targets;
            }

            var seen = new HashSet<string>();
            foreach (var edge in outgoingEdges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.Target))
                {
                    continue;
                }

                if (!CarriedItems(outputCountsByBranch, edge.SourceHandle))
                {
                    continue;
                }

                if (seen.Add(edge.Target))
                {
                    targets.Add(edge.Target);
                }
            }

            return targets;
        }

        /// <summary>
        /// True when every incoming parent has either finished or cannot still run.
        /// A parent with no row blocks only when some other active or running node can reach it.
        /// The walk does not start at <paramref name="nodeId"/> and does not leave a failed node.
        /// A completed node is left only through handles that carried items.
        /// </summary>
        public static bool IsReady(
            string nodeId,
            IReadOnlyList<EdgeEnity>? edges,
            IReadOnlyList<NodeExecutionEntity>? executions,
            IReadOnlyCollection<string>? activeNodeIds)
        {
            var graph = edges ?? Array.Empty<EdgeEnity>();
            var incoming = graph.Where(e => e != null && e.Target == nodeId).ToList();
            if (incoming.Count == 0)
            {
                return true;
            }

            var latestByNode = LatestByNode(executions);
            var active = activeNodeIds ?? Array.Empty<string>();
            var reachable = NodesReachableFromActiveWork(nodeId, graph, latestByNode, active);

            foreach (var edge in incoming)
            {
                if (ParentCanStillRun(edge.Source, latestByNode, active, reachable))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ParentCanStillRun(
            string parentId,
            Dictionary<string, NodeExecutionEntity> latestByNode,
            IReadOnlyCollection<string> activeNodeIds,
            HashSet<string> reachable)
        {
            if (latestByNode.TryGetValue(parentId, out var row)
                && row.Status is NodeExecutionStatus.Completed or NodeExecutionStatus.Failed)
            {
                return false;
            }

            if (row != null && row.Status == NodeExecutionStatus.Running)
            {
                return true;
            }

            if (activeNodeIds.Contains(parentId))
            {
                return true;
            }

            return reachable.Contains(parentId);
        }

        private static HashSet<string> NodesReachableFromActiveWork(
            string nodeId,
            IReadOnlyList<EdgeEnity> edges,
            Dictionary<string, NodeExecutionEntity> latestByNode,
            IReadOnlyCollection<string> activeNodeIds)
        {
            var edgesBySource = edges
                .Where(e => e != null && !string.IsNullOrEmpty(e.Source))
                .GroupBy(e => e.Source)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EdgeEnity>)g.ToList());

            var reachable = new HashSet<string>();
            var queue = new Queue<string>();

            foreach (var id in activeNodeIds)
            {
                if (!string.IsNullOrEmpty(id) && id != nodeId)
                {
                    queue.Enqueue(id);
                }
            }

            foreach (var row in latestByNode.Values)
            {
                if (row.Status == NodeExecutionStatus.Running && row.NodeId != nodeId)
                {
                    queue.Enqueue(row.NodeId);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!reachable.Add(current) || current == nodeId)
                {
                    continue;
                }

                if (!edgesBySource.TryGetValue(current, out var outgoing))
                {
                    continue;
                }

                var followOnlyTaken = false;
                NodeExecutionEntity? row = null;
                if (latestByNode.TryGetValue(current, out row))
                {
                    if (row.Status == NodeExecutionStatus.Failed)
                    {
                        continue;
                    }

                    followOnlyTaken = row.Status == NodeExecutionStatus.Completed;
                }

                foreach (var edge in outgoing)
                {
                    if (followOnlyTaken && !CarriedItems(row!.OutputCountsByBranch, edge.SourceHandle))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(edge.Target))
                    {
                        queue.Enqueue(edge.Target);
                    }
                }
            }

            return reachable;
        }

        private static Dictionary<string, NodeExecutionEntity> LatestByNode(IReadOnlyList<NodeExecutionEntity>? executions)
        {
            var latest = new Dictionary<string, NodeExecutionEntity>();
            if (executions == null)
            {
                return latest;
            }

            foreach (var row in executions)
            {
                if (row == null || string.IsNullOrEmpty(row.NodeId))
                {
                    continue;
                }

                if (!latest.TryGetValue(row.NodeId, out var existing) || row.RunIndex >= existing.RunIndex)
                {
                    latest[row.NodeId] = row;
                }
            }

            return latest;
        }
    }
}
