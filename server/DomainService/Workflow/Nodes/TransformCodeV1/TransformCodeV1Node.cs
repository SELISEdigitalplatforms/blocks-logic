using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using DomainService.Workflow.Entities;
using Jint;
using Jint.Native;
using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DomainService.Workflow.Nodes.TransformCodeV1
{
    [ExcludeFromCodeCoverage]
    public class TransformCodeV1Node : NodeExecutorBase<TransformCodeV1Parameters>
    {
        public override string NodeType => "code";
        public override string Version => "v1";

        private const int MaxScriptLengthBytes = 256 * 1024;
        private const int MaxTotalDurationSeconds = 120;

        private const int DefaultTimeoutSeconds = 30;
        private const long DefaultMemoryLimitBytes = 32 * 1024 * 1024;
        private const int MaxRecursionDepth = 64;
        private const int MaxStatements = 1_000_000;

        private const int MaxOutputItems = 10_000;
        private const long MaxSerializedOutputBytes = 16 * 1024 * 1024;

        public const string SourceIdKey = "__id";

        private static readonly MongoDB.Bson.IO.JsonWriterSettings BsonJsonSettings = new()
        {
            OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson,
        };


        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TransformCodeV1Parameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new TransformCodeV1Parameters();
                var script = parameters.Script ?? string.Empty;

                if (parameters.Language != "js")
                {
                    return NodeExecutionResult.Failed($"Language '{parameters.Language}' is not supported. Only JS is supported.");
                }
                if (Encoding.UTF8.GetByteCount(script) > MaxScriptLengthBytes)
                {
                    return NodeExecutionResult.Failed("Script too large.");
                }

                return parameters.Mode == "each"
                    ? RunPerItem(context, parameters, script)
                    : RunOnceForAllItems(context, parameters, script);
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        private NodeExecutionResult RunPerItem(NodeExecutionContext context, TransformCodeV1Parameters parameters, string script)
        {
            var stopwatch = Stopwatch.StartNew();
            var outputItems = new List<NodeOutputItem>();
            long serializedOutputBytes = 0;

            var inputItems = context.InputItems;
            var ancestorsById = IndexAncestorsById(context);

            for (var i = 0; i < inputItems.Count; i++)
            {
                if (stopwatch.Elapsed > TimeSpan.FromSeconds(MaxTotalDurationSeconds))
                {
                    return NodeExecutionResult.Failed("Script execution exceeded max duration.");
                }
                var current = inputItems[i];
                var item = ToJObject(current.Data.Output ?? new BsonDocument(), current.Id);
                var node = GenerateAncestorPerItem(ancestorsById, current);
                var engine = CreateEngine(context, node, item);

                JsValue result;
                try
                {
                    result = engine.Evaluate(WrapScript(script));
                }
                catch (Exception ex)
                {
                    return NodeExecutionResult.Failed(FormatScriptError(ex));
                }

                var normalized = NormalizeResult(result);

                foreach (var token in normalized)
                {
                    if (outputItems.Count + 1 > MaxOutputItems)
                    {
                        return NodeExecutionResult.Failed("Too many output items.");
                    }

                    var (outputToken, _) = ExtractOutputAndSourceId(token);

                    var serializationError = SerializePayload(outputToken, ref serializedOutputBytes, out var bsonValue);
                    if (serializationError is not null)
                    {
                        return NodeExecutionResult.Failed(serializationError);
                    }

                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData
                        {
                            Input = current.Data.Output,
                            Output = bsonValue,
                            Parameters = parameters.ToBsonDocument(),
                        },
                        Branch = "source",
                        ParentItemIds = new List<string> { current.Id },
                    });
                }
            }

            return NodeExecutionResult.Successful(outputItems);
        }

        private NodeExecutionResult RunOnceForAllItems(NodeExecutionContext context, TransformCodeV1Parameters parameters, string script)
        {
            var engine = CreateEngine(context);
            JsValue result;
            try
            {
                result = engine.Evaluate(WrapScript(script));
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(FormatScriptError(ex));
            }

            var normalized = NormalizeResult(result);
            if (normalized.Count > MaxOutputItems)
            {
                return NodeExecutionResult.Failed("Too many output items.");
            }

            var outputItems = new List<NodeOutputItem>();
            long serializedOutputBytes = 0;

            foreach (var token in normalized)
            {
                if (outputItems.Count + 1 > MaxOutputItems)
                {
                    return NodeExecutionResult.Failed("Too many output items.");
                }

                var (outputToken, sourceId) = ExtractOutputAndSourceId(token);

                var serializationError = SerializePayload(outputToken, ref serializedOutputBytes, out var bsonValue);
                if (serializationError is not null)
                {
                    return NodeExecutionResult.Failed(serializationError);
                }

                var parentItem = !string.IsNullOrEmpty(sourceId)
                    ? context.InputItems.FirstOrDefault(i => i.Id == sourceId)
                    : null;

                List<string> parentIds;
                string branch;
                BsonValue input;
                if (parentItem is not null)
                {
                    parentIds = new List<string> { parentItem.Id };
                    branch = parentItem.Branch;
                    input = parentItem.Data?.Output ?? new BsonDocument();
                }
                else
                {
                    parentIds = context.InputItems.Count > 0
                        ? context.InputItems.Select(i => i.Id).ToList()
                        : new List<string>();
                    branch = "source";
                    input = new BsonDocument();
                }

                outputItems.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = input,
                        Output = bsonValue,
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = branch,
                    ParentItemIds = parentIds,
                });
            }

            return NodeExecutionResult.Successful(outputItems);
        }



        #region Engine
        private Engine CreateEngine(NodeExecutionContext context, JObject node, JObject item)
        {
            var engine = NewEngine(context);
            engine.Execute($"var $json = {item};");
            engine.Execute($"var $item = {item};");
            // Inject as JSON (same as $json/$items) so arrays stay arrays and ISO dates
            // stay strings. SetValue(JObject) wraps CLR tokens; returning them
            // converts arrays to { "0": ... } and Date values to {}.
            engine.Execute($"var $node = {node};");
            engine.SetValue("console", CodeNodeConsole);
            return engine;
        }

        private Engine CreateEngine(NodeExecutionContext context)
        {
            var engine = NewEngine(context);
            var itemsArray = new JArray(
                context.InputItems.Select(i => ToN8nItem(i.Data.Output ?? new BsonDocument(), i.Id)));
            engine.Execute($"var $items = {itemsArray};");
            engine.Execute(BuildNodeAccessors(context));
            engine.SetValue("console", CodeNodeConsole);
            return engine;
        }

        /// <summary>
        /// All-mode <c>$node</c>: each ancestor node name maps to an accessor with
        /// <c>all()</c>/<c>first()</c>/<c>last()</c>/<c>item(i)</c> returning n8n-style
        /// items <c>{ json, __id }</c>. Mirrors n8n's <c>$('&lt;Node&gt;').all()</c>.
        /// </summary>
        private static string BuildNodeAccessors(NodeExecutionContext context)
        {
            var sb = new StringBuilder("var $node = {");
            var any = false;
            foreach (var (nodeName, items) in context.AncestorNodeOutputs)
            {
                if (items is null || items.Count == 0) continue;
                if (any) sb.Append(',');
                any = true;

                var arr = new JArray(items.Select(AncestorEntry));
                var key = Newtonsoft.Json.JsonConvert.ToString(nodeName);
                sb.Append(key);
                sb.Append(":(function(){var __i=");
                sb.Append(arr);
                sb.Append(";return{all:function(){return __i;},first:function(){return __i[0];},last:function(){return __i[__i.length-1];},item:function(i){return __i[i];}};})()");
            }
            sb.Append("};");
            return any ? sb.ToString() : "var $node = {};";
        }

        private static readonly string[] BlockedGlobals =
        {
            "require", "process", "fetch", "XMLHttpRequest",
            "http", "https", "fs", "child_process", "net", "dns",
            "global", "GLOBAL", "root",
        };

        private static Engine NewEngine(NodeExecutionContext context)
        {
            var engine = new Engine(options => options
                // No AllowClr(): scripts cannot reach System.* / CLR types.
                .TimeoutInterval(TimeSpan.FromSeconds(DefaultTimeoutSeconds))
                .LimitMemory(DefaultMemoryLimitBytes)
                .LimitRecursion(MaxRecursionDepth)
                .MaxStatements(MaxStatements)
                .CancellationToken(context.CancellationToken));

            // Defense-in-depth: neutralize the denylist on the global object.
            foreach (var name in BlockedGlobals)
            {
                engine.Execute($"delete globalThis['{name}'];");
            }

            return engine;
        }

        private static readonly object CodeNodeConsole = new
        {
            log = (Action<object?>)(msg => Console.WriteLine($"[code-node] {msg}")),
            warn = (Action<object?>)(msg => Console.WriteLine($"[code-node][warn] {msg}")),
            error = (Action<object?>)(msg => Console.Error.WriteLine($"[code-node][error] {msg}")),
        };


        private static JObject ToJObject(BsonValue value, string itemId)
        {
            var token = BsonValueToJToken(value);
            if (token is JObject obj)
            {
                obj[SourceIdKey] = itemId;
                return obj;
            }
            return new JObject { ["json"] = token, [SourceIdKey] = itemId };
        }

        /// <summary>
        /// Builds an n8n-style item: <c>{ json: { ...data, __id } }</c>. Scripts access
        /// fields as <c>$items[i].json.&lt;field&gt;</c> and the source id as
        /// <c>$items[i].json.__id</c>.
        /// </summary>
        private static JObject ToN8nItem(BsonValue output, string itemId)
        {
            var json = BsonValueToJToken(output);
            if (json is not JObject payload)
            {
                payload = new JObject { ["value"] = json };
            }
            payload[SourceIdKey] = itemId;
            return new JObject { ["json"] = payload };
        }

        private static Dictionary<string, WorkflowItemExecutionEntity> IndexAncestorsById(NodeExecutionContext context)
        {
            var byId = new Dictionary<string, WorkflowItemExecutionEntity>();
            foreach (var items in context.AncestorNodeOutputs.Values)
            {
                if (items is null) continue;
                foreach (var it in items)
                {
                    byId[it.Id] = it;
                }
            }
            return byId;
        }

        private static JObject AncestorEntry(WorkflowItemExecutionEntity ancestor)
            => new()
            {
                ["json"] = BsonValueToJToken(ancestor.Data?.Output ?? new BsonDocument()),
            };

        private static JObject GenerateAncestorPerItem(IReadOnlyDictionary<string, WorkflowItemExecutionEntity> ancestorsById, WorkflowItemExecutionEntity item)
        {
            var node = new JObject();
            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(item.Id);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!visited.Add(id)) continue;
                if (!ancestorsById.TryGetValue(id, out var ancestor)) continue;

                node[ancestor.NodeName] = AncestorEntry(ancestor);

                if (ancestor.ParentItemIds is null) continue;
                foreach (var parentId in ancestor.ParentItemIds)
                {
                    if (!string.IsNullOrEmpty(parentId))
                    {
                        queue.Enqueue(parentId);
                    }
                }
            }
            return node;
        }

        #endregion

        #region Output extraction & lineage resolution

        /// <summary>
        /// Extracts the persistable output and source input id from a script-returned
        /// value. Recognises the n8n item shape <c>{ json: { ...data, __id } }</c>
        /// (unwrapping <c>json</c> and reading <c>__id</c> from inside it) and also
        /// supports bare objects carrying a top-level <c>__id</c>.
        /// </summary>
        private static (JToken Output, string? SourceId) ExtractOutputAndSourceId(JToken token)
        {
            // n8n item wrapper: { json: { ...data, __id } }.
            if (token is JObject outer && outer["json"] is JObject payload)
            {
                var sourceId = payload.Value<string>(SourceIdKey);
                var clone = (JObject)payload.DeepClone();
                clone.Remove(SourceIdKey);
                return (clone, sourceId);
            }

            // Bare object: { ...data, __id? }.
            if (token is JObject obj)
            {
                var sourceId = obj.Value<string>(SourceIdKey);
                var clone = (JObject)obj.DeepClone();
                clone.Remove(SourceIdKey);
                return (clone, sourceId);
            }

            // Non-object primitives/arrays wrap under { json: ... }.
            return (new JObject { ["json"] = token.DeepClone() }, null);
        }

        #endregion

        #region Output assembly

        private static string? SerializePayload(JToken payload, ref long accumulatedBytes, out BsonValue value)
        {
            if (payload == null || payload.Type == JTokenType.Null)
            {
                value = new BsonDocument();
                return null;
            }
            var json = payload.ToString(Newtonsoft.Json.Formatting.None);
            accumulatedBytes += Encoding.UTF8.GetByteCount(json);
            value = BsonDocument.Parse(json);
            return accumulatedBytes > MaxSerializedOutputBytes ? "Output too large." : null;
        }

        #endregion

        #region Script wrappers & mode resolution

        private static string WrapScript(string script)
            => string.IsNullOrWhiteSpace(script) ? "({})" : $"(() => {{ {script} }})()";

        private static string FormatScriptError(Exception ex)
        {
            var message = ex.Message ?? ex.GetType().Name;
            var inner = ex.InnerException?.Message;
            return string.IsNullOrEmpty(inner) ? message : $"{message} ({inner})";
        }

        #endregion

        #region Result normalization

        private static List<JToken> NormalizeResult(JsValue value)
        {
            var items = new List<JToken>();
            if (value.IsNull() || value.IsUndefined())
            {
                return items;
            }
            var token = JsValueToJToken(value);
            if (token is JArray jarr)
            {
                items.AddRange(jarr);
            }
            else
            {
                items.Add(token);
            }
            return items;
        }

        #endregion

        #region Jint <-> JToken <-> Bson conversion

        private static JToken JsValueToJToken(JsValue value)
        {
            if (value.IsNull() || value.IsUndefined()) return JValue.CreateNull();
            if (value.IsBoolean()) return new JValue(value.AsBoolean());
            if (value.IsNumber()) return new JValue(value.AsNumber());
            if (value.IsString()) return new JValue(value.AsString());
            if (value.IsDate())
            {
                return new JValue(value.AsDate().ToDateTime().ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            }
            if (value.IsObject())
            {
                var obj = value.AsObject();
                if (obj is JsArray jsArray)
                {
                    var arr = new JArray();
                    var length = jsArray.Get("length").AsNumber();
                    for (var i = 0; i < length; i++)
                    {
                        arr.Add(JsValueToJToken(jsArray.Get(JsValue.FromObject(jsArray.Engine, i))));
                    }
                    return arr;
                }

                var result = new JObject();
                foreach (var kvp in obj.GetOwnProperties())
                {
                    if (!kvp.Value.Enumerable) continue;
                    var key = kvp.Key.AsString();
                    if (string.IsNullOrEmpty(key)) continue;
                    if (kvp.Value.Value is null) continue;
                    result[key] = JsValueToJToken(kvp.Value.Value);
                }
                return result;
            }
            return JValue.CreateNull();
        }

        private static JToken BsonValueToJToken(BsonValue value)
        {
            var json = value.ToJson(BsonJsonSettings);
            if (string.IsNullOrEmpty(json)) return JValue.CreateNull();
            using var reader = new JsonTextReader(new StringReader(json))
            {
                DateParseHandling = DateParseHandling.None,
            };
            return JToken.Load(reader);
        }

        #endregion
    }
}
