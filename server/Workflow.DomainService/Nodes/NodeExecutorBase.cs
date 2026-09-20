using Workflow.DomainService.Entities;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proxy.DomainService.Services;

namespace Workflow.DomainService.Nodes
{
    public abstract class NodeExecutorBase<TParameters> : INodeExecutor
    {
        public abstract string NodeType { get; }
        public abstract string Version { get; }

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        // Grep handle for the {{$VAR.name}} trace below. Kept as a literal prefix inside the message
        // template (not a structured field) so a plain text search over the log sink finds every line.
        private const string VariableResolveLogCategory = "Workflow.VariableResolve";

        // Mirrors Proxy.DomainService.Utils.ProxyVarRef's syntax so a {{$VAR.name}} token means the same
        // thing everywhere in Blocks: the literal "{{$VAR." prefix, a name in [A-Za-z0-9._:-], then "}}".
        private static readonly Regex VariableReference =
            new(@"\{\{\$VAR\.([A-Za-z0-9._:-]+)\}\}", RegexOptions.None, RegexTimeout);

        // Saved node parameters can carry an explicit JSON null for a field that used to be, or was
        // never, set (e.g. an older workflow saved before a "haveQuery" toggle existed). Newtonsoft
        // cannot assign null to a non-nullable value type (bool, int, ...) and throws instead of
        // falling back to the property's declared default, so nulls are ignored on read here.
        private static readonly Newtonsoft.Json.JsonSerializerSettings ParameterDeserializationSettings = new()
        {
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
        };

        protected abstract Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TParameters? parameters);

        /// <summary>
        /// Builds a synthetic output item describing a caught exception, for inclusion in
        /// NodeExecutionResult.Failed alongside any items already produced. Never throws itself.
        /// </summary>
        protected static NodeOutputItem? TryBuildErrorOutputItem(
            WorkflowItemExecutionEntity? inputItem, BsonValue parameters, Exception ex, string branch = "source")
        {
            try
            {
                return new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = inputItem?.Data.Output ?? new BsonDocument(),
                        Output = new BsonDocument
                        {
                            { "error", true },
                            { "message", ex.Message ?? ex.GetType().Name },
                        },
                        Parameters = parameters ?? new BsonDocument(),
                    },
                    Branch = branch,
                    ParentItemIds = !string.IsNullOrEmpty(inputItem?.Id)
                        ? new List<string> { inputItem!.Id }
                        : new List<string>(),
                };
            }
            catch
            {
                return null; // best-effort; never let error-item construction mask the real failure
            }
        }

        protected static NodeOutputItem? TryBuildErrorOutputItem(
            WorkflowItemExecutionEntity? inputItem, BsonValue parameters, string message, string branch = "source")
            => TryBuildErrorOutputItem(inputItem, parameters, new Exception(message), branch);

        protected static void AppendErrorOutputItem(
            List<NodeOutputItem> outputItems,
            WorkflowItemExecutionEntity? inputItem,
            BsonValue parameters,
            Exception ex)
        {
            var item = TryBuildErrorOutputItem(inputItem, parameters, ex);
            if (item != null) outputItems.Add(item);
        }

        protected static void AppendErrorOutputItem(
            List<NodeOutputItem> outputItems,
            WorkflowItemExecutionEntity? inputItem,
            BsonValue parameters,
            string message)
        {
            var item = TryBuildErrorOutputItem(inputItem, parameters, message);
            if (item != null) outputItems.Add(item);
        }

        public async Task<NodeExecutionResult> RunAsync(NodeExecutionContext context)
        {
            var json = context.Parameters.ToJson();

            var variableNames = CollectVariableNames(context.Parameters).ToList();

            var logger = context.ServiceProvider?.GetService<ILoggerFactory>()?.CreateLogger(VariableResolveLogCategory);
            var nodeName = string.IsNullOrEmpty(context.NodeName) ? context.NodeId : context.NodeName;

            // Emitted for EVERY node, including nodes that collected nothing. Without it, "this node has no
            // {{$VAR}} tokens" and "this node has tokens the collector failed to see" produce identical output
            // (silence), and those are exactly the two cases we are trying to tell apart. HasVarToken is a raw
            // substring test over the serialized parameters, deliberately independent of the collector regex:
            // HasVarToken true with Count 0 means the collector missed a token that is really there.
            logger?.LogInformation(
                "variable resolve log: node {NodeName} collected {Count} variable(s) [{Names}], parameters contain a $VAR token: {HasVarToken}",
                nodeName,
                variableNames.Count,
                string.Join(", ", variableNames),
                json.Contains("$VAR", StringComparison.Ordinal));

            if (variableNames.Count > 0)
            {
                foreach (var name in variableNames)
                {
                    logger?.LogInformation(
                        "variable resolve log: resolving variable {Name} for node {NodeName}", name, nodeName);
                }

                var resolver = context.ServiceProvider?.GetService<IProxyVariableResolver>();
                if (resolver is null)
                {
                    return NodeExecutionResult.Failed(
                        "This node references {{$VAR.name}} configuration variable(s), but no variable resolver is available in this environment.");
                }

                try
                {
                    var resolvedVariables = await resolver.ResolveAsync(variableNames, context.TenantId, context.CancellationToken);
                    var unresolved = variableNames
                        .Where(name => !resolvedVariables.ContainsKey(name))
                        .ToList();

                    if (unresolved.Count > 0)
                    {
                        return NodeExecutionResult.Failed(
                            $"Could not resolve configuration variable(s): {string.Join(", ", unresolved)}.");
                    }

                    foreach (var name in variableNames)
                    {
                        // Only the NAME is ever logged, never the value: these are Key Vault secrets and the
                        // log sink is not a place they may land. An empty value is reported as its own line
                        // rather than silently omitted, so a missing pair in the trace always means the node
                        // never reached this point at all.
                        if (!string.IsNullOrEmpty(resolvedVariables[name]))
                        {
                            logger?.LogInformation(
                                "variable resolve log: resolved variable {Name} for node {NodeName}", name, nodeName);
                        }
                        else
                        {
                            logger?.LogWarning(
                                "variable resolve log: variable {Name} for node {NodeName} resolved to an EMPTY value",
                                name, nodeName);
                        }
                    }

                    context.ResolvedVariables = resolvedVariables;
                }
                catch (ProxyVariableResolutionException ex)
                {
                    return NodeExecutionResult.Failed(
                        $"Could not resolve configuration variable(s): {string.Join(", ", ex.Names)}.");
                }
            }

            var parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<TParameters>(json, ParameterDeserializationSettings);
            return await ExecuteAsync(context, parameters);
        }

        private static IEnumerable<string> CollectVariableNames(BsonValue value)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in CollectVariableNamesCore(value))
            {
                if (seen.Add(name))
                {
                    yield return name;
                }
            }
        }

        private static IEnumerable<string> CollectVariableNamesCore(BsonValue value)
        {
            if (value == null || value.IsBsonNull)
            {
                yield break;
            }

            if (value.IsString)
            {
                foreach (Match match in VariableReference.Matches(value.AsString))
                {
                    yield return match.Groups[1].Value;
                }

                yield break;
            }

            if (value.IsBsonDocument)
            {
                foreach (var element in value.AsBsonDocument)
                {
                    foreach (var name in CollectVariableNamesCore(element.Value))
                    {
                        yield return name;
                    }
                }

                yield break;
            }

            if (value.IsBsonArray)
            {
                foreach (var item in value.AsBsonArray)
                {
                    foreach (var name in CollectVariableNamesCore(item))
                    {
                        yield return name;
                    }
                }
            }
        }

        /// <summary>
        /// Parse expressions like {{$json.fieldName}} from input items
        /// Supports:
        /// - {{$json.field}} - current item data
        /// - {{$context.key}} - workflow context
        /// - {{$node["nodeName"].json.field}} - ancestor node output (automatically resolves via lineage)
        /// </summary>
        protected T? parseExpression<T>(string text, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            if (string.IsNullOrEmpty(text)) return default;

            var resolved = Regex.Replace(text, @"\{\{([^{}]+)\}\}", match =>
                ResolveExpression(match.Groups[1].Value.Trim(), inputItem, context), RegexOptions.None, RegexTimeout);

            if (typeof(T) == typeof(string)) return (T)(object)resolved;
            if (typeof(T) == typeof(object))
            {
                try
                {
                    return (T)Newtonsoft.Json.JsonConvert.DeserializeObject(resolved)!;
                }
                catch
                {
                    return (T)(object)resolved;
                }
            }

            try { return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(resolved); }
            catch { return default; }
        }

        private static string ResolveExpression(string expr, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {

            if (expr.StartsWith("$node"))
                return ResolveNodeReference(expr, inputItem, context);

            if (expr.StartsWith("$json"))
                return ResolveJsonExpression(expr, inputItem, context);

            if (expr.StartsWith("$context"))
                return ResolveContextExpression(expr, context);

            if (expr.StartsWith("$VAR."))
                return ResolveVarExpression(expr, context);

            return "";
        }

        /// <summary>
        /// Looks up a {{$VAR.name}} configuration variable in <see cref="NodeExecutionContext.ResolvedVariables"/>,
        /// which <see cref="RunAsync"/> populated (via <see cref="IProxyVariableResolver"/>) before this node's
        /// parameters were deserialized. Never resolves on demand: every name in the node's parameters was
        /// already resolved once, up front, or the node execution failed before reaching here.
        /// </summary>
        private static string ResolveVarExpression(string expr, NodeExecutionContext context)
        {
            var name = expr.Substring("$VAR.".Length);
            if (context.ResolvedVariables.TryGetValue(name, out var value))
            {
                return value;
            }

            throw new InvalidOperationException(
                $"Configuration variable '{name}' was referenced during expression parsing, but it was not resolved before node execution.");
        }

        private static string ResolveNodeReference(string expr, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            var nodeMatch = Regex.Match(expr, @"^\$node\[""(?<node>[^""]+)""\]\.json\.output\.(?<path>.+)$", RegexOptions.None, RegexTimeout);
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

        private static string ResolveJsonExpression(string expr, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
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
            return selected.Type switch
            {
                JTokenType.String => selected.Value<string>() ?? "",
                JTokenType.Boolean => selected.Value<bool>().ToString().ToLower(), // "false" / "true"
                JTokenType.Null => "null",
                // Objects/arrays stay as JSON strings so downstream deserialize works
                JTokenType.Object or JTokenType.Array => selected.ToString(Newtonsoft.Json.Formatting.None),
                // Integers, floats, etc — use Newtonsoft's serialization, not .ToString()
                _ => Newtonsoft.Json.JsonConvert.SerializeObject(selected.ToObject<object>())
            };
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
                }, RegexOptions.None, RegexTimeout);
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
            if (value == null) return "";
            return value.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
        }

        private static string BsonToJson(BsonDocument doc) =>
            doc.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
    }
}
