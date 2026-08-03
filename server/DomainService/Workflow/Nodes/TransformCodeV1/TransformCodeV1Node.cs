using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DomainService.Workflow.Entities;
using Jint;
using Jint.Native;
using Jint.Native.Object;
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
        private const int MaxTotalDurationSeconds = 60;

        private const int DefaultTimeoutSeconds = 30;
        private const long DefaultMemoryLimitBytes = 32 * 1024 * 1024;
        private const int MaxRecursionDepth = 64;
        private const int MaxStatements = 1_000_000;


        private const int MaxOutputItems = 10_000;
        private const long MaxSerializedOutputBytes = 16 * 1024 * 1024;

        public TransformCodeV1Node() { }

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TransformCodeV1Parameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new TransformCodeV1Parameters();
                var script = parameters.Script ?? string.Empty;

                var lang = parameters.Language?.Trim().ToLowerInvariant();
                if (lang != "js" && lang != "javascript")
                {
                    return NodeExecutionResult.Failed($"Language '{parameters.Language}' is not supported. Only 'javascript' is supported.");
                }

                if (Encoding.UTF8.GetByteCount(script) > MaxScriptLengthBytes)
                {
                    return NodeExecutionResult.Failed("Script too large.");
                }

                var inputs = GenerateInputPayload(context);

                if (parameters.Mode == "each")
                    return RunPerItem(context, parameters, script, inputs);

                return RunOnceForAllItems(context, parameters, script, inputs);
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        private NodeExecutionResult RunPerItem(NodeExecutionContext context, TransformCodeV1Parameters parameters, string script, List<JToken> inputs)
        {

            var stopwatch = Stopwatch.StartNew();
            var outputItems = new List<NodeOutputItem>();
            long serializedOutputBytes = 0;

            for (var i = 0; i < inputs.Count; i++)
            {

                if (stopwatch.Elapsed > TimeSpan.FromSeconds(MaxTotalDurationSeconds))
                {
                    return NodeExecutionResult.Failed("Script execution exceeded max duration.");
                }

                var item = inputs[i]!;
                var engine = CreateEngine(context, inputs, item, i);

                JsValue perItemResult;
                try
                {
                    perItemResult = engine.Evaluate(WrapPerItemScript(script));
                }
                catch (Exception ex)
                {
                    return NodeExecutionResult.Failed(FormatScriptError(ex));
                }

                var perItemPayloadResult = NormalizePerItemResult(perItemResult);
                if (perItemPayloadResult.Error is not null)
                {
                    return NodeExecutionResult.Failed(perItemPayloadResult.Error);
                }
                var perItemPayload = perItemPayloadResult.Value!;
                if (outputItems.Count + 1 > MaxOutputItems)
                {
                    return NodeExecutionResult.Failed("Too many output items.");
                }

                var (bsonValue, bytes) = JTokenToBsonValue(perItemPayload, serializedOutputBytes);
                serializedOutputBytes = bytes;
                if (serializedOutputBytes > MaxSerializedOutputBytes)
                {
                    return NodeExecutionResult.Failed("Output too large.");
                }

                var inputItem = i < context.InputItems.Count ? context.InputItems[i] : null;
                outputItems.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = inputItem?.Data.Output ?? new BsonDocument(),
                        Output = bsonValue,
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = "source",
                    ParentItemIds = inputItem != null
                        ? new List<string> { inputItem.Id }
                        : new List<string>(),
                });
            }

            return NodeExecutionResult.Successful(outputItems);
        }


        private NodeExecutionResult RunOnceForAllItems(
            NodeExecutionContext context,
            TransformCodeV1Parameters parameters,
            string script,
            List<JToken> inputs)
        {


            var engine = CreateEngine(context, inputs);
            JsValue result;
            try
            {
                result = engine.Evaluate(WrapScript(script));
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(FormatScriptError(ex));
            }

            if (!result.IsObject() || result.AsObject() is not JsArray)
            {
                return NodeExecutionResult.Failed("Code node in 'all' mode must return an array of items.");
            }

            var normalized = NormalizeAllItemsResult(result);
            if (normalized.Count > MaxOutputItems)
            {
                return NodeExecutionResult.Failed("Too many output items.");
            }

            var outputItems = new List<NodeOutputItem>();
            var parentIds = context.InputItems.Select(i => i.Id).ToList();
            long serializedOutputBytes = 0;
            foreach (var token in normalized)
            {
                var (bsonValue, bytes) = JTokenToBsonValue(token, serializedOutputBytes);
                serializedOutputBytes = bytes;
                if (serializedOutputBytes > MaxSerializedOutputBytes)
                {
                    return NodeExecutionResult.Failed("Output too large.");
                }

                outputItems.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = new BsonDocument(),
                        Output = bsonValue,
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = "source",
                    ParentItemIds = parentIds.Count > 0 ? new List<string>(parentIds) : null,
                });
            }

            return NodeExecutionResult.Successful(outputItems);
        }


        private static Engine CreateEngine(
            NodeExecutionContext context,
            List<JToken> inputs,
            JToken? perItem = null,
            int perItemIndex = 0)
        {
            var engine = new Engine(options =>
            {
                options
                    .TimeoutInterval(TimeSpan.FromSeconds(DefaultTimeoutSeconds))
                    .LimitMemory(DefaultMemoryLimitBytes)
                    .LimitRecursion(MaxRecursionDepth)
                    .MaxStatements(MaxStatements)
                    .CancellationToken(context.CancellationToken);
            });

            // Sandbox: no AllowClr() — Jint is locked out of System.* / CLR by default.
            // Build $items as a real JS array by evaluating a JSON literal so the
            // user can do `return $items;` and have it survive the round-trip as an
            // array of objects. SetValue on List<JToken> would wrap it as a
            // GenericListWrapper which is NOT a JsArray.
            var itemsJson = JsonConvert.SerializeObject(inputs);
            engine.Execute($"var $items = {itemsJson};");

            var perItemValue = perItem ?? (inputs.Count > 0 ? inputs[0] : JValue.CreateNull());
            var perItemJson = JsonConvert.SerializeObject(perItemValue);
            engine.Execute($"var $input = {perItemJson};");
            engine.Execute($"var $json = {perItemJson};");
            engine.Execute($"var $item = {perItemJson};");

            engine.SetValue("$itemIndex", perItemIndex);
            engine.SetValue("$node", GenerateAncestor(context));
            engine.SetValue("console", GenerateConsole());
            return engine;
        }

        private static List<JToken> GenerateInputPayload(NodeExecutionContext context)
        {
            var list = new List<JToken>();
            foreach (var item in context.InputItems)
            {
                var output = item.Data?.Output ?? new BsonDocument();
                list.Add(BsonValueToJToken(output));
            }
            return list;
        }

        private static JObject GenerateAncestor(NodeExecutionContext context)
        {
            var node = new JObject();
            foreach (var entry in context.AncestorNodeOutputs)
            {
                var items = entry.Value ?? new List<WorkflowItemExecutionEntity>();
                var arr = new JArray();
                foreach (var it in items)
                {
                    arr.Add(BsonValueToJToken(it.Data?.Output ?? new BsonDocument()));
                }
                node[entry.Key] = arr;
            }
            return node;
        }

        private static object GenerateConsole()
        {
            return new
            {
                log = new Action<object?>(msg => Console.WriteLine($"[code-node] {msg}")),
                warn = new Action<object?>(msg => Console.WriteLine($"[code-node][warn] {msg}")),
                error = new Action<object?>(msg => Console.Error.WriteLine($"[code-node][error] {msg}")),
            };
        }

        private static string WrapScript(string script)
        {
            if (string.IsNullOrWhiteSpace(script)) return "({})";
            return $"(() => {{ {script} }})()";
        }

        private static string WrapPerItemScript(string script) => WrapScript(script);

        private static List<JToken> NormalizeAllItemsResult(JsValue value)
        {
            if (value.IsObject() && value.AsObject() is JsArray)
            {
                var items = new List<JToken>();
                foreach (var element in EnumerateJsArray(value.AsObject()))
                {
                    items.Add(JsValueToJToken(element));
                }
                return items;
            }

            if (value.IsObject())
            {
                return new List<JToken> { JsValueToJToken(value) };
            }

            return new List<JToken> { new JObject { ["value"] = JsValueToJToken(value) } };
        }

        private static (JToken? Value, string? Error) NormalizePerItemResult(JsValue value)
        {
            if (!value.IsObject() || value.AsObject() is JsArray)
            {
                return (null, "Code node in 'each' mode must return a single object.");
            }

            return (JsValueToJToken(value), null);
        }

        private static IEnumerable<JsValue> EnumerateJsArray(ObjectInstance array)
        {
            var len = array.Get("length").AsNumber();
            var engine = array.Engine;
            for (var i = 0; i < len; i++)
            {
                yield return array.Get(JsValue.FromObject(engine, i));
            }
        }

        private static JToken JsValueToJToken(JsValue value)
        {
            if (value.IsNull() || value.IsUndefined()) return JValue.CreateNull();
            if (value.IsBoolean()) return new JValue(value.AsBoolean());
            if (value.IsNumber()) return new JValue(value.AsNumber());
            if (value.IsString()) return new JValue(value.AsString());
            if (value.IsObject())
            {
                var obj = value.AsObject();
                if (obj is JsArray)
                {
                    var arr = new JArray();
                    foreach (var el in EnumerateJsArray(obj))
                    {
                        arr.Add(JsValueToJToken(el));
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
            var json = value.ToJson(new MongoDB.Bson.IO.JsonWriterSettings
            {
                OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson,
            });
            return string.IsNullOrEmpty(json) ? JValue.CreateNull() : JToken.Parse(json);
        }

        private static (BsonValue value, long bytes) JTokenToBsonValue(JToken token, long accumulatedBytes)
        {
            if (token == null) return (new BsonDocument(), accumulatedBytes);
            var json = token.ToString(Newtonsoft.Json.Formatting.None);
            var bytes = accumulatedBytes + Encoding.UTF8.GetByteCount(json);
            return (BsonDocument.Parse(json), bytes);
        }

        private static string FormatScriptError(Exception ex)
        {
            var message = ex.Message ?? ex.GetType().Name;
            var inner = ex.InnerException?.Message;
            return string.IsNullOrEmpty(inner) ? message : $"{message} ({inner})";
        }
    }
}