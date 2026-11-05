using Blocks.Genesis.Entities.Logic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Workflow.DomainService.Nodes.ActionStorageV2;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// Hides Storage download URLs in execution views (SPEC11 D4). A Get item's <c>url</c> is a pre-signed link that
    /// works until it expires, so the editor shows it as <see cref="Redacted"/>: the URL itself and every string equal
    /// to it, in any item of the execution, at any depth (a later node's <c>Input</c>, a resolved parameter, a copied
    /// field). The saved items are never changed: later nodes and resumed runs still read the real URL, and the
    /// webhook response is not masked (D6).
    /// </summary>
    public static class ExecutionUrlMasker
    {
        public const string Redacted = "[redacted]";
        private const string UrlField = "url";

        /// <summary>Node ids of the Storage nodes in a workflow snapshot.</summary>
        public static HashSet<string> StorageNodeIds(IEnumerable<(string Id, string Type)> nodes) =>
            nodes.Where(n => string.Equals(n.Type, ActionStorageV2Node.Type, StringComparison.Ordinal))
                 .Select(n => n.Id)
                 .ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// The items to show: unchanged when nothing needs masking, otherwise masked copies of the items that held a
        /// URL. Uses only the items given; the input list and its entities are never mutated.
        /// </summary>
        public static List<WorkflowItemExecutionEntity> Mask(
            List<WorkflowItemExecutionEntity> items, IReadOnlySet<string> storageNodeIds)
        {
            if (items.Count == 0 || storageNodeIds.Count == 0) return items;

            var urls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items.Where(i => i.NodeId is not null && storageNodeIds.Contains(i.NodeId)))
            {
                if (item.Data?.Output is BsonDocument output
                    && output.TryGetValue(UrlField, out var url) && url.IsString && url.AsString.Length > 0)
                {
                    urls.Add(url.AsString);
                }
            }
            if (urls.Count == 0) return items;

            return items.Select(item => MaskItem(item, urls)).ToList();
        }

        private static WorkflowItemExecutionEntity MaskItem(WorkflowItemExecutionEntity item, HashSet<string> urls)
        {
            var document = item.ToBsonDocument();
            return Replace(document, urls)
                ? BsonSerializer.Deserialize<WorkflowItemExecutionEntity>(document)
                : item;
        }

        /// <summary>Replaces matching strings in place; true when anything was replaced.</summary>
        private static bool Replace(BsonValue value, HashSet<string> urls)
        {
            var changed = false;
            if (value is BsonDocument document)
            {
                foreach (var element in document.Elements.ToList())
                {
                    changed |= ReplaceChild(element.Value, v => document[element.Name] = v, urls);
                }
            }
            else if (value is BsonArray array)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    var index = i;
                    changed |= ReplaceChild(array[i], v => array[index] = v, urls);
                }
            }
            return changed;
        }

        private static bool ReplaceChild(BsonValue child, Action<BsonValue> set, HashSet<string> urls)
        {
            if (!IsUrl(child, urls)) return Replace(child, urls);
            set(Redacted);
            return true;
        }

        private static bool IsUrl(BsonValue value, HashSet<string> urls) => value.IsString && urls.Contains(value.AsString);
    }
}
