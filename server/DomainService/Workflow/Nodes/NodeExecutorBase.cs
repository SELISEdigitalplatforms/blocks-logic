using DomainService.Workflow.Models;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace DomainService.Workflow.Nodes
{
    public abstract class NodeExecutorBase<TParameters> : INodeExecutor
    {
        public abstract string NodeType { get; }
        public abstract string Version { get; }

        protected abstract Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TParameters? parameters);

        public async Task<NodeExecutionResult> RunAsync(NodeExecutionContext context)
        {
            var json = context.Parameters.ToJson();
            var parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<TParameters>(json);
            return await ExecuteAsync(context, parameters);
        }

        /// <summary>
        /// Parse expressions like {{$json.fieldName}} from input items
        /// Supports:
        /// - {{$json.field}} - current item data
        /// - {{$context.key}} - workflow context
        /// - {{$node["nodeName"].json.field}} - ancestor node output (automatically resolves via lineage)
        /// </summary>
        protected T? parseExpression<T>(string text, WorkflowItemExecutionModel inputItem, NodeExecutionContext context)
        {
            if (string.IsNullOrEmpty(text)) return default;


            var resolved = Regex.Replace(text, @"\{\{([^{}]+)\}\}", match => ResolveExpression(match.Groups[1].Value.Trim(), inputItem, context));

            if (typeof(T) == typeof(string)) return (T)(object)resolved;

            try { return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(resolved); }
            catch { return default; }
        }

        private static string ResolveExpression(string expr, WorkflowItemExecutionModel inputItem, NodeExecutionContext context)
        {

            if (expr.StartsWith("$node"))
                return ResolveNodeReference(expr, inputItem, context);

            if (expr.StartsWith("$json"))
                return ResolveJsonExpression(expr, inputItem, context);

            if (expr.StartsWith("$context"))
                return ResolveContextExpression(expr, context);

            return "";
        }

        private static string ResolveNodeReference(string expr, WorkflowItemExecutionModel inputItem, NodeExecutionContext context)
        {
            var nodeMatch = Regex.Match(expr, @"^\$node\[""(?<node>[^""]+)""\]\.json\.output\.(?<path>.+)$");
            var nodeName = nodeMatch.Groups["node"].Value;
            var path = nodeMatch.Groups["path"].Value;

            if (!inputItem.AncestorMap.TryGetValue(nodeName, out var ancestorId) || ancestorId == null)
                return "";
            var ancestorItem = context.AncestorNodeOutputs.TryGetValue(nodeName, out var items)
                ? items.FirstOrDefault(i => i.Id == ancestorId)
                : null;

            if (ancestorItem == null) return "";
            return SelectPath(ancestorItem.Data, path);
        }

        private static string ResolveJsonExpression(string expr, WorkflowItemExecutionModel inputItem, NodeExecutionContext context)
        {
            var path = expr.Length > 13 ? expr.Substring(13) : "";
            if (string.IsNullOrEmpty(path))
                return BsonValueToJson(inputItem.Data.Output);
            return SelectOutputPath(inputItem.Data.Output, path);
        }

        private static string ResolveContextExpression(string expr, NodeExecutionContext context)
        {
            var key = expr.Substring(9);
            return context.WorkflowContext.Contains(key)
                ? context.WorkflowContext[key]?.ToString() ?? ""
                : "";
        }

        /// <summary>
        /// Navigate a dot-path within the Output BsonValue (not the full NodeOutputItemData).
        /// </summary>
        private static string SelectOutputPath(BsonValue output, string path)
        {
            var json = BsonValueToJson(output);
            var token = JToken.Parse(json);
            var selected = token.SelectToken(path);
            return selected?.Type == JTokenType.String ? selected.Value<string>() ?? "" : selected?.ToString() ?? "";
        }

        /// <summary>
        /// Called by ResolveNodeReference to navigate ancestor output.
        /// </summary>
        private static string SelectPath(NodeOutputItemData data, string path)
        {
            return SelectOutputPath(data.Output, path);
        }

        /// <summary>
        /// Normalize FE-stored expression format to BE-resolvable format.
        /// FE stores: node_{id}_{handle}.field (no {{ }})
        /// BE expects: {{$node["NodeName"].json.field}}
        /// </summary>
        private string NormalizeExpression(string text, NodeExecutionContext context)
        {
            return Regex.Replace(text, @"(?:\{\{)?node_([a-zA-Z0-9\-]+)_(\w+)(\.([\w.]+))?(?:\}\})?",
                match =>
                {
                    var nodeId = match.Groups[1].Value;
                    var fieldPath = match.Groups[4].Value;
                    var nodeName = FindNodeNameById(nodeId, context);
                    if (nodeName == null) return match.Value;
                    return string.IsNullOrEmpty(fieldPath)
                        ? $"{{{{$node[\"{nodeName}\"].json}}}}"
                        : $"{{{{$node[\"{nodeName}\"].json.{fieldPath}}}}}";
                });
        }

        /// <summary>
        /// Look up node name from AncestorNodeOutputs by NodeId.
        /// </summary>
        private static string? FindNodeNameById(string nodeId, NodeExecutionContext context)
        {
            if (context.AncestorNodeOutputs == null) return null;
            foreach (var items in context.AncestorNodeOutputs.Values)
            {
                var item = items.FirstOrDefault(i => i.NodeId == nodeId);
                if (item != null) return item.NodeName;
            }
            // Also check current input items (for $json-like references via node ID)
            var inputItem = context.InputItems.FirstOrDefault(i => i.NodeId == nodeId);
            return inputItem?.NodeName;
        }

        private static string BsonValueToJson(BsonValue value)
        {
            if (value is BsonDocument doc)
                return doc.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
            if (value is BsonArray arr)
                return arr.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
            return value?.ToString() ?? "";
        }

        private static string BsonToJson(BsonDocument doc) =>
            doc.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
    }
}
