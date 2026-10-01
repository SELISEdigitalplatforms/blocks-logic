using Workflow.DomainService.Enums;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// Removes a finished node from the active set and adds its children as one transition.
    /// A parallel completion must apply this whole result, never the empty set in between.
    /// </summary>
    public static class ActiveNodeCompletion
    {
        public static ActiveNodeCompletionResult Apply(
            IReadOnlyCollection<string>? activeNodeIds,
            string completedNodeId,
            IReadOnlyCollection<string>? nextNodeIds,
            WorkflowExecutionStatus status)
        {
            var active = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (activeNodeIds != null)
            {
                foreach (var id in activeNodeIds)
                {
                    if (string.IsNullOrWhiteSpace(id) || id == completedNodeId)
                    {
                        continue;
                    }

                    if (seen.Add(id))
                    {
                        active.Add(id);
                    }
                }
            }

            if (nextNodeIds != null)
            {
                foreach (var id in nextNodeIds)
                {
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    if (seen.Add(id))
                    {
                        active.Add(id);
                    }
                }
            }

            var becameComplete = active.Count == 0
                && status != WorkflowExecutionStatus.Failed
                && status != WorkflowExecutionStatus.Completed;

            return new ActiveNodeCompletionResult(
                active,
                becameComplete ? WorkflowExecutionStatus.Completed : status,
                becameComplete);
        }
    }

    public sealed class ActiveNodeCompletionResult
    {
        public ActiveNodeCompletionResult(
            IReadOnlyList<string> activeNodeIds,
            WorkflowExecutionStatus status,
            bool becameComplete)
        {
            ActiveNodeIds = activeNodeIds;
            Status = status;
            BecameComplete = becameComplete;
        }

        public IReadOnlyList<string> ActiveNodeIds { get; }

        public WorkflowExecutionStatus Status { get; }

        public bool BecameComplete { get; }
    }
}
