using System.Diagnostics.CodeAnalysis;
using DomainService.Workflow.Utils;
using MongoDB.Bson;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DomainService.Workflow.Nodes.TransformSetFieldV1
{
    [ExcludeFromCodeCoverage]
    public class TransformSetFieldV1Node : NodeExecutorBase<TransformSetFieldV1Parameters>
    {
        public override string NodeType => "setfield";
        public override string Version => "v1";

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TransformSetFieldV1Parameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new TransformSetFieldV1Parameters();
                var outputItems = new List<NodeOutputItem>();

                for (int i = 0; i < context.IterationCount; i++)
                {
                    var inputElement = BsonJsonConverter.ToJsonElement(context.InputItems[i].Data.Output);
                    var mode = parameters.Mode.ToLower();
                    var currentItem = GetBaseItem(inputElement, parameters);
                    var output = new JsonObject();

                    if (mode == "manual_mapping" && parameters.ManualMappingFields != null && parameters.ManualMappingFields.Count > 0)
                    {
                        output = ParsedMappedValue(parameters.ManualMappingFields, context.InputItems[i], context);
                    }
                    else if (mode == "json")
                    {
                        output = ParseJsonValue(parameters.JsonCode, context.InputItems[i], context);
                    }


                    foreach (var prop in output) currentItem[prop.Key] = prop.Value?.DeepClone();

                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData
                        {
                            Input = context.InputItems[i].Data.Output,
                            Output = BsonJsonConverter.ToBsonValue(currentItem.Deserialize<JsonElement>()),
                            Parameters = parameters.ToBsonDocument(),
                        },
                        Branch = "source",
                        ParentItemIds = new List<string>() { context.InputItems[i].Id },
                    });
                }
                return NodeExecutionResult.Successful(outputItems);
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        private JsonObject ParsedMappedValue(
            List<ManualMappingField> fields,
            Models.WorkflowItemExecutionModel inputItem,
            NodeExecutionContext context)
        {
            var result = new JsonObject();
            foreach (var field in fields)
            {
                var key = parseExpression<string>(field.key, inputItem, context);
                if (string.IsNullOrWhiteSpace(key)) continue;

                result[key] = field.type switch
                {
                    "string" => JsonValue.Create(parseExpression<string>(field.value, inputItem, context)),
                    "number" => JsonValue.Create(parseExpression<double>(field.value, inputItem, context)),
                    "boolean" => JsonValue.Create(parseExpression<bool>(field.value, inputItem, context)),
                    "json" => JsonNode.Parse(field.value),
                    _ => JsonValue.Create(parseExpression<object>(field.value, inputItem, context)?.ToString())
                };
            }
            return result;
        }

        private JsonObject ParseJsonValue(
       string jsonCode,
       Models.WorkflowItemExecutionModel inputItem,
       NodeExecutionContext context)
        {
            var result = new JsonObject();
            var entries = jsonCode.Trim().TrimStart('{').TrimEnd('}').Split(',');

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;

                var parts = entry.Trim().Split(':', 2);
                if (parts.Length < 2) continue;

                var rawKey = parts[0].Trim().Trim('"');
                var key = parseExpression<object>(rawKey, inputItem, context)?.ToString();
                var value = parseExpression<object>(parts[1].Trim(), inputItem, context)?.ToString();

                if (string.IsNullOrWhiteSpace(key)) continue;

                result[key] = JsonValue.Create(value);
            }

            return result;
        }

        private JsonObject FilterInput(JsonElement input, List<string> fields, bool include)
        {
            var result = new JsonObject();
            foreach (var prop in input.EnumerateObject())
            {
                bool shouldInclude = include ? fields.Contains(prop.Name) : !fields.Contains(prop.Name);
                if (shouldInclude)
                    result[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
            }
            return result;
        }

        private JsonElement MergeJsonElements(JsonElement @base, JsonElement patch)
        {
            var baseNode = JsonNode.Parse(@base.GetRawText()) as JsonObject ?? new JsonObject();
            var patchNode = JsonNode.Parse(patch.GetRawText()) as JsonObject ?? new JsonObject();

            foreach (var prop in patchNode)
                baseNode[prop.Key] = prop.Value?.DeepClone();

            return JsonSerializer.Deserialize<JsonElement>(baseNode.ToJsonString());
        }

        private JsonObject GetBaseItem(JsonElement inputElement, TransformSetFieldV1Parameters parameters)
        {
            if (!parameters.IncludeOtherFields)
                return new JsonObject();

            var otherFieldsMode = parameters.OtherFieldsMode?.ToLower();

            return otherFieldsMode switch
            {
                "include" => FilterInput(
                    inputElement,
                    parameters.IncludedFields.Split(',').Select(f => f.Trim()).ToList(),
                    include: true),

                "exclude" => FilterInput(
                    inputElement,
                    parameters.ExcludeFields.Split(',').Select(f => f.Trim()).ToList(),
                    include: false),

                _ => JsonNode.Parse(inputElement.GetRawText()) as JsonObject ?? new JsonObject()
            };
        }
    }
}
