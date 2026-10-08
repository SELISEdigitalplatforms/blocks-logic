using Workflow.DomainService.Entities;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Proxy.DomainService.Services;
using Workflow.DomainService.Logging;

namespace Workflow.DomainService.Nodes
{
    public abstract class NodeExecutorBase<TParameters> : INodeExecutor
    {
        public abstract string NodeType { get; }
        public abstract string Version { get; }

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

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

        /// <summary>Whether <paramref name="item"/> is a synthetic error item built by <see cref="TryBuildErrorOutputItem(WorkflowItemExecutionEntity?, BsonValue, Exception, string)"/>.</summary>
        protected static bool IsErrorItem(NodeOutputItem item)
            => item.Data?.Output is BsonDocument output
               && output.TryGetValue("error", out var error)
               && error.IsBoolean && error.AsBoolean;

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
            // Stage lines carry counts only: never variable names, parameter values or item data.
            var log = context.Log;
            var json = context.Parameters.ToJson();

            var variableNames = CollectVariableNames(context.Parameters).ToList();

            if (variableNames.Count > 0)
            {
                log.Info(ExecutionLogStages.NodeVariables, "Resolving {Count} configuration variable(s).", variableNames.Count);

                var resolver = context.ServiceProvider?.GetService<IProxyVariableResolver>();
                if (resolver is null)
                {
                    log.Error(ExecutionLogStages.NodeVariablesFailed, "No variable resolver is available.");
                    return NodeExecutionResult.Failed(
                        "This node references {{$VAR.name}} configuration variable(s), but no variable resolver is available in this environment.");
                }

                try
                {
                    var resolvedVariables = await resolver.ResolveAsync(variableNames, context.TenantId, ct: context.CancellationToken);
                    var unresolved = variableNames
                        .Where(name => !resolvedVariables.ContainsKey(name))
                        .ToList();

                    if (unresolved.Count > 0)
                    {
                        log.Error(ExecutionLogStages.NodeVariablesFailed, "{Count} configuration variable(s) could not be resolved.", unresolved.Count);
                        return NodeExecutionResult.Failed(
                            $"Could not resolve configuration variable(s): {string.Join(", ", unresolved)}.");
                    }

                    context.ResolvedVariables = resolvedVariables;
                    log.Info(ExecutionLogStages.NodeVariablesResolved, "Configuration variables resolved.");
                }
                catch (ProxyVariableResolutionException ex)
                {
                    log.Error(ExecutionLogStages.NodeVariablesFailed, "{Count} configuration variable(s) could not be resolved.", ex.Names.Count);
                    return NodeExecutionResult.Failed(
                        $"Could not resolve configuration variable(s): {string.Join(", ", ex.Names)}.");
                }
            }

            TParameters? parameters;
            try
            {
                parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<TParameters>(json, ParameterDeserializationSettings);
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                // A saved parameter has the wrong JSON shape (e.g. an array where an object of text values
                // is expected; workflows built through the API are not shape-checked on save). Fail this step
                // with the field path and expected vs actual shape. Newtonsoft's own message is never shown:
                // it quotes the offending value, which can be a secret.
                log.Error(ExecutionLogStages.NodeParametersFailed, "Parameters could not be read ({ErrorKind:l}).", ex.GetType().Name);
                return NodeExecutionResult.Failed(
                    NodeParameterShape.Describe(json, typeof(TParameters)) ?? NodeParameterShape.Fallback(JsonPath(ex)));
            }
            catch (Exception ex)
            {
                log.Error(ExecutionLogStages.NodeParametersFailed, "Parameters could not be read ({ErrorKind:l}).", ex.GetType().Name);
                throw;
            }
            log.Info(ExecutionLogStages.NodeParameters, "Parameters loaded.");

            log.Info(ExecutionLogStages.NodeExecuting, "Running {NodeType:l} logic on {Count} item(s).", NodeType, context.InputItems.Count);
            var result = await ExecuteAsync(context, parameters);
            if (result.IsSuccess)
            {
                log.Info(ExecutionLogStages.NodeExecuted, "Logic finished: {Count} output item(s).", result.OutputItems?.Count ?? 0);
            }
            else
            {
                log.Error(ExecutionLogStages.NodeExecuted, "Logic reported a failure.");
            }
            return result;
        }

        private static string? JsonPath(Newtonsoft.Json.JsonException ex) => ex switch
        {
            Newtonsoft.Json.JsonSerializationException s => s.Path,
            Newtonsoft.Json.JsonReaderException r => r.Path,
            _ => null,
        };

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
            => ResolveExpressionValue(expr, inputItem, context).Text;

        /// <summary>
        /// What one <c>{{expr}}</c> resolved to. <see cref="Typed"/> is set for <c>$json</c> / <c>$node</c>
        /// paths (the stored BSON value, so its type is known); text-only sources (<c>$context</c>,
        /// <c>$VAR</c>) leave it null. <see cref="Found"/> is false for a missing path, a missing context
        /// key, or an unknown expression; <see cref="Text"/> is then empty.
        /// </summary>
        private readonly record struct ExpressionValue(bool Found, BsonValue? Typed, string Text)
        {
            public static readonly ExpressionValue Missing = new(false, null, "");

            public static ExpressionValue FromBson(BsonValue? value)
                => value is null ? Missing : new(true, value, FormatSelectedBsonValue(value));
        }

        private static ExpressionValue ResolveExpressionValue(string expr, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            if (expr.StartsWith("$node"))
                return ExpressionValue.FromBson(SelectNodeReference(expr, inputItem, context));

            if (expr.StartsWith("$json"))
                return ExpressionValue.FromBson(SelectJsonExpression(expr, inputItem));

            if (expr.StartsWith("$context"))
                return ResolveContextExpressionValue(expr, context);

            if (expr.StartsWith("$VAR."))
                return new ExpressionValue(true, null, ResolveVarExpression(expr, context));

            return ExpressionValue.Missing;
        }

        private enum TemplateKind { Json, GraphQl }

        /// <summary>
        /// Fills the <c>{{…}}</c> values of a JSON template (Set Field JSON, Function input, HTTP / Proxy
        /// JSON body) so the result stays JSON whatever the values contain.
        /// <list type="bullet">
        /// <item>Inside a "…" string literal: the value's text, JSON-escaped (no quotes added).
        /// A missing value is empty.</item>
        /// <item>Outside a string, a <c>$json</c> / <c>$node</c> value is written as JSON by its stored type:
        /// a string is quoted and escaped (so <c>"123"</c> stays a string), numbers / booleans / null as
        /// themselves, objects and arrays as JSON.</item>
        /// <item>Outside a string, a text-only value (<c>$context</c>, <c>$VAR</c>) is inserted as is when
        /// it already parses as JSON, else as an escaped JSON string.</item>
        /// <item>Outside a string, a missing value is <c>null</c>.</item>
        /// </list>
        /// The result is not validated here; see <see cref="FillJsonTemplateOrThrow"/>.
        /// </summary>
        protected string ResolveJsonTemplate(string template, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
            => FillTemplate(template, inputItem, context, TemplateKind.Json);

        /// <summary>
        /// <see cref="ResolveJsonTemplate"/>, then checks the result is valid JSON. Throws
        /// <see cref="InvalidFilledJsonException"/> naming <paramref name="fieldLabel"/> and the error
        /// position when it is not; nodes turn that into a failed step.
        /// </summary>
        protected string FillJsonTemplateOrThrow(
            string? template, string fieldLabel, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            var filled = ResolveJsonTemplate(template ?? string.Empty, inputItem, context);
            if (!JsonTemplateText.TryValidate(filled, out var error))
                throw new InvalidFilledJsonException($"{fieldLabel} is not valid JSON after filling in values: {error}");
            return filled;
        }

        /// <summary>
        /// Fills the <c>{{…}}</c> values of a raw GraphQL query. Inside a "…" string literal the value is
        /// escaped the same way as JSON, so a <c>"</c> in a value cannot end the literal. Outside a
        /// string the value's text is inserted exactly as before. Not validated.
        /// </summary>
        protected string ResolveGraphQlTemplate(string template, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
            => FillTemplate(template, inputItem, context, TemplateKind.GraphQl);

        private static string FillTemplate(
            string template, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context, TemplateKind kind)
        {
            if (string.IsNullOrEmpty(template)) return template ?? string.Empty;

            var sb = new StringBuilder(template.Length + 16);
            var inString = false;
            var i = 0;
            while (i < template.Length)
            {
                var c = template[i];

                // Same token shape as parseExpression: "{{", one or more non-brace characters, "}}".
                // The expression itself is skipped as a whole, so quotes inside it
                // ($node["Name"]) never change the string state.
                if (c == '{' && TryReadExpression(template, i, out var expr, out var next))
                {
                    var value = ResolveExpressionValue(expr.Trim(), inputItem, context);
                    if (inString)
                        JsonTemplateText.AppendEscaped(sb, value.Found ? value.Text : string.Empty);
                    else if (kind == TemplateKind.GraphQl)
                        sb.Append(value.Text);
                    else
                        sb.Append(ToJsonValue(value));
                    i = next;
                    continue;
                }

                sb.Append(c);
                if (inString)
                {
                    if (c == '\\' && i + 1 < template.Length)
                    {
                        // An escaped character (\" included) never ends the string.
                        sb.Append(template[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (c == '"') inString = false;
                }
                else if (c == '"')
                {
                    inString = true;
                }
                i++;
            }
            return sb.ToString();
        }

        private static bool TryReadExpression(string text, int start, out string expr, out int next)
        {
            expr = string.Empty;
            next = start;
            if (start + 1 >= text.Length || text[start + 1] != '{') return false;

            var k = start + 2;
            while (k < text.Length && text[k] != '{' && text[k] != '}') k++;
            if (k == start + 2 || k + 1 >= text.Length || text[k] != '}' || text[k + 1] != '}') return false;

            expr = text.Substring(start + 2, k - start - 2);
            next = k + 2;
            return true;
        }

        /// <summary>A value written outside a JSON string literal (see <see cref="ResolveJsonTemplate"/>).</summary>
        private static string ToJsonValue(ExpressionValue value)
        {
            if (!value.Found) return "null";
            if (value.Typed is not null) return BsonToJsonLiteral(value.Typed);
            return JsonTemplateText.IsValidJson(value.Text) ? value.Text : JsonTemplateText.Quote(value.Text);
        }

        private static string BsonToJsonLiteral(BsonValue value)
        {
            switch (value.BsonType)
            {
                case BsonType.Null:
                case BsonType.Undefined:
                    return "null";
                case BsonType.String:
                    return JsonTemplateText.Quote(value.AsString);
                case BsonType.Boolean:
                    return value.AsBoolean ? "true" : "false";
                case BsonType.Int32:
                    return value.AsInt32.ToString(CultureInfo.InvariantCulture);
                case BsonType.Int64:
                    return value.AsInt64.ToString(CultureInfo.InvariantCulture);
                case BsonType.Double:
                    var d = value.AsDouble;
                    // NaN / Infinity have no JSON number form; keep them readable as strings.
                    return double.IsFinite(d)
                        ? Newtonsoft.Json.JsonConvert.SerializeObject(d)
                        : JsonTemplateText.Quote(d.ToString(CultureInfo.InvariantCulture));
                case BsonType.Decimal128:
                    var text = value.AsDecimal128.ToString();
                    return JsonTemplateText.IsValidJson(text) ? text : JsonTemplateText.Quote(text);
                default:
                    // Documents, arrays, dates, ids: relaxed extended JSON is valid JSON.
                    return value.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
            }
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
            => ResolveContextExpressionValue(expr, context).Text;

        private static ExpressionValue ResolveContextExpressionValue(string expr, NodeExecutionContext context)
        {
            var key = expr.Length > 9 ? expr.Substring(9) : "";
            return context.WorkflowContext.Contains(key)
                ? new ExpressionValue(true, null, context.WorkflowContext[key]?.ToString() ?? "")
                : ExpressionValue.Missing;
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

        /// <summary>
        /// Same ancestor lookup as <see cref="ResolveNodeReference"/>, but the path after
        /// <c>.json.output</c> may be empty, <c>.field</c>, or <c>[index]</c>. Null when missing.
        /// </summary>
        private static BsonValue? SelectNodeReference(string expr, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            var nodeMatch = Regex.Match(
                expr,
                @"^\$node\[""(?<node>[^""]+)""\]\.json\.output(?<path>(?:\.(?<dotted>.+)|(?<bracket>\[.+))?)$",
                RegexOptions.None,
                RegexTimeout);
            if (!nodeMatch.Success)
                return null;

            var nodeName = nodeMatch.Groups["node"].Value;
            var path = nodeMatch.Groups["dotted"].Success
                ? nodeMatch.Groups["dotted"].Value
                : nodeMatch.Groups["bracket"].Value;

            if (!inputItem.AncestorMap.TryGetValue(nodeName, out var ancestorId) || ancestorId == null)
                return null;
            var ancestorItem = context.AncestorNodeOutputs.TryGetValue(nodeName, out var items)
                ? items.FirstOrDefault(i => i.Id == ancestorId)
                : null;

            if (ancestorItem?.Data?.Output is null)
                return null;
            return TrySelectBsonPath(ancestorItem.Data.Output, path, out var selected) ? selected : null;
        }

        /// <summary>
        /// Reads a <c>$json</c> expression on the current item's output. Null when missing.
        /// <list type="bullet">
        /// <item><c>$json</c> and <c>$json.output</c>: the whole item.</item>
        /// <item><c>$json.output.x</c> / <c>$json.output[0]</c>: path on the item (the original form).</item>
        /// <item><c>$json.x</c> / <c>$json[0]</c>: the same as <c>$json.output.x</c> / <c>$json.output[0]</c>.</item>
        /// <item>Anything else starting with <c>$json</c> (e.g. <c>$jsonfoo</c>): missing.</item>
        /// </list>
        /// Because <c>$json.output.x</c> keeps its meaning, an item field literally named <c>output</c>
        /// is read with <c>$json.output.output</c> (<c>$json.output</c> alone is the whole item).
        /// A dot right before the next segment is optional: <c>$json.output.ids[0]</c> keeps the index.
        /// </summary>
        private static BsonValue? SelectJsonExpression(string expr, WorkflowItemExecutionEntity inputItem)
        {
            var output = inputItem.Data?.Output;
            if (output is null)
                return null;

            string path;
            if (expr == "$json" || expr == "$json.output")
                path = "";
            else if (expr.StartsWith("$json.output.", StringComparison.Ordinal))
                path = expr.Substring("$json.output.".Length);
            else if (expr.StartsWith("$json.output[", StringComparison.Ordinal))
                path = expr.Substring("$json.output".Length);
            else if (expr.StartsWith("$json.", StringComparison.Ordinal))
                path = expr.Substring("$json.".Length);
            else if (expr.StartsWith("$json[", StringComparison.Ordinal))
                path = expr.Substring("$json".Length);
            else
                return null;

            return TrySelectBsonPath(output, path, out var selected) ? selected : null;
        }

        /// <summary>
        /// Walks <paramref name="path"/> on <paramref name="output"/> using <see cref="BsonType"/>.
        /// A <see cref="BsonType.Array"/> consumes <c>[n]</c>. A <see cref="BsonType.Document"/> consumes a property name.
        /// A dot immediately before <c>[</c> is ignored. Missing names and out-of-range indexes return false.
        /// An empty path selects <paramref name="output"/> itself.
        /// </summary>
        private static bool TrySelectBsonPath(BsonValue output, string path, out BsonValue selected)
        {
            selected = output;
            if (output is null)
                return false;
            if (string.IsNullOrEmpty(path))
                return true;

            var current = output;
            var index = 0;
            while (index < path.Length)
            {
                if (path[index] == '.')
                {
                    index++;
                    continue;
                }

                if (path[index] == '[')
                {
                    if (!TryReadPathIndex(path, ref index, out var elementIndex) || !TrySelectArrayElement(current, elementIndex, out current))
                        return false;
                    continue;
                }

                if (!TryReadPathName(path, ref index, out var name) || !TrySelectProperty(current, name, out current))
                    return false;
            }

            selected = current;
            return true;
        }

        private static bool TryReadPathIndex(string path, ref int index, out int elementIndex)
        {
            elementIndex = -1;
            var close = path.IndexOf(']', index + 1);
            if (close < 0)
                return false;

            var raw = path.Substring(index + 1, close - index - 1);
            if (raw.Length == 0 || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out elementIndex) || elementIndex < 0)
                return false;

            index = close + 1;
            return true;
        }

        private static bool TryReadPathName(string path, ref int index, out string name)
        {
            var start = index;
            while (index < path.Length && path[index] != '.' && path[index] != '[')
                index++;

            if (index == start)
            {
                name = "";
                return false;
            }

            name = path.Substring(start, index - start);
            return true;
        }

        private static bool TrySelectProperty(BsonValue current, string name, out BsonValue selected)
        {
            if (current is not null && current.IsBsonDocument && current.AsBsonDocument.TryGetValue(name, out var value))
            {
                selected = value;
                return true;
            }

            selected = BsonNull.Value;
            return false;
        }

        private static bool TrySelectArrayElement(BsonValue current, int elementIndex, out BsonValue selected)
        {
            if (current is not null && current.IsBsonArray)
            {
                var array = current.AsBsonArray;
                if ((uint)elementIndex < (uint)array.Count)
                {
                    selected = array[elementIndex];
                    return true;
                }
            }

            selected = BsonNull.Value;
            return false;
        }

        /// <summary>
        /// Leaf text for a selected BSON value. Documents, arrays, and extended types stay JSON.
        /// Strings, booleans, null, and numbers are plain text.
        /// </summary>
        private static string FormatSelectedBsonValue(BsonValue? value)
        {
            if (value is null || value.IsBsonNull)
                return "null";

            return value.BsonType switch
            {
                BsonType.String => value.AsString,
                BsonType.Boolean => value.AsBoolean ? "true" : "false",
                BsonType.Int32 => value.AsInt32.ToString(CultureInfo.InvariantCulture),
                BsonType.Int64 => value.AsInt64.ToString(CultureInfo.InvariantCulture),
                BsonType.Double => Newtonsoft.Json.JsonConvert.SerializeObject(value.AsDouble),
                BsonType.Decimal128 => value.AsDecimal.ToString(CultureInfo.InvariantCulture),
                _ => value.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson }),
            };
        }
    }
}
