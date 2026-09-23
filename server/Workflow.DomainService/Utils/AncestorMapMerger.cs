using Workflow.DomainService.Entities;

namespace Workflow.DomainService.Utils
{
    /// <summary>
    /// Builds the ancestor map stored on an output item. A node name is kept only when every
    /// resolved parent points at the same item id. Disagreeing names are omitted so a singular
    /// <c>$node["Name"]</c> expression does not pick an arbitrary item.
    /// </summary>
    public static class AncestorMapMerger
    {
        public static Dictionary<string, string> Merge(
            IEnumerable<string>? parentIds,
            IEnumerable<WorkflowItemExecutionEntity>? candidates,
            string selfItemId,
            string selfNodeName)
        {
            var byId = IndexFirstById(candidates);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var conflicts = new HashSet<string>(StringComparer.Ordinal);

            if (parentIds != null)
            {
                foreach (var parentId in parentIds)
                {
                    if (string.IsNullOrEmpty(parentId) || !byId.TryGetValue(parentId, out var parent))
                    {
                        continue;
                    }

                    Contribute(values, conflicts, parent);
                }
            }

            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in values)
            {
                if (!conflicts.Contains(key))
                {
                    merged[key] = value;
                }
            }

            merged[selfNodeName] = selfItemId;
            return merged;
        }

        private static Dictionary<string, WorkflowItemExecutionEntity> IndexFirstById(
            IEnumerable<WorkflowItemExecutionEntity>? candidates)
        {
            var byId = new Dictionary<string, WorkflowItemExecutionEntity>(StringComparer.Ordinal);
            if (candidates == null)
            {
                return byId;
            }

            foreach (var candidate in candidates)
            {
                if (candidate == null || string.IsNullOrEmpty(candidate.Id))
                {
                    continue;
                }

                if (!byId.ContainsKey(candidate.Id))
                {
                    byId[candidate.Id] = candidate;
                }
            }

            return byId;
        }

        private static void Contribute(
            Dictionary<string, string> values,
            HashSet<string> conflicts,
            WorkflowItemExecutionEntity parent)
        {
            var map = parent.AncestorMap != null
                ? new Dictionary<string, string>(parent.AncestorMap, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            map[parent.NodeName] = parent.Id;

            foreach (var (key, value) in map)
            {
                if (conflicts.Contains(key))
                {
                    continue;
                }

                if (!values.TryGetValue(key, out var existing))
                {
                    values[key] = value;
                    continue;
                }

                if (!string.Equals(existing, value, StringComparison.Ordinal))
                {
                    conflicts.Add(key);
                    values.Remove(key);
                }
            }
        }
    }
}
