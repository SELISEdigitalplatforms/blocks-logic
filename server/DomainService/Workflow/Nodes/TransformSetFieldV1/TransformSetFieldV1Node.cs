using System.Diagnostics.CodeAnalysis;
using DomainService.Workflow.Utils;
using MongoDB.Bson;
using System.Text.Json;
using System.Text.Json.Nodes;
using DomainService.Workflow.Entities;

namespace DomainService.Workflow.Nodes.TransformSetFieldV1
{
    [ExcludeFromCodeCoverage]
    public class TransformSetFieldV1Node : NodeExecutorBase<TransformSetFieldV1Parameters>
    {
        public override string NodeType => "setfield";
        public override string Version => "v1";

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

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
            WorkflowItemExecutionEntity inputItem,
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
        WorkflowItemExecutionEntity inputItem,
        NodeExecutionContext context)
        {
            var resolved = System.Text.RegularExpressions.Regex.Replace(jsonCode, @"\{\{(.+?)\}\}", match =>
            {
                var value = parseExpression<object>(match.Value, inputItem, context);
                if (value == null) return "null";
                var strValue = value switch
                {
                    string s => s,
                    bool b => b.ToString().ToLower(),
                    _ => Newtonsoft.Json.JsonConvert.SerializeObject(value)
                };

                try
                {
                    JsonDocument.Parse(strValue);
                    return strValue;
                }
                catch
                {
                    return $"\"{strValue}\"";
                }
            }, System.Text.RegularExpressions.RegexOptions.None, RegexTimeout);

            return JsonNode.Parse(resolved)!.AsObject();
        }

        private JsonObject FilterInput(JsonElement input, List<string> fields, bool include)
        {
            var result = new JsonObject();

            if (fields == null || fields.Count == 0)
            {
                if (include) return new JsonObject();
                return JsonNode.Parse(input.GetRawText()) as JsonObject ?? new JsonObject();
            }

            foreach (var prop in input.EnumerateObject())
            {
                var nestedFields = fields
                    .Where(f => f.StartsWith(prop.Name + "."))
                    .Select(f => f[(prop.Name.Length + 1)..])
                    .ToList();

                if (nestedFields.Any())
                {
                    var nested = FilterInput(prop.Value, nestedFields, include);
                    foreach (var nestedProp in nested)
                        result[nestedProp.Key] = nestedProp.Value?.DeepClone();
                }
                else
                {
                    bool shouldInclude = include ? fields.Contains(prop.Name) : !fields.Contains(prop.Name);
                    if (shouldInclude)
                        result[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
                }
            }

            return result;
        }

        private JsonObject GetBaseItem(JsonElement inputElement, TransformSetFieldV1Parameters parameters)
        {
            if (!parameters.IncludeOtherFields)
                return new JsonObject();

            var otherFieldsMode = parameters.OtherFieldsMode?.ToLower();
            var includedFields = parameters.IncludedFields?.Split(',').Select(f => f.Trim()).ToList() ?? new List<string>();
            var excludedFields = parameters.ExcludeFields?.Split(',').Select(f => f.Trim()).ToList() ?? new List<string>();

            return otherFieldsMode switch
            {
                "include" => FilterInput(inputElement, includedFields, include: true),
                "exclude" => FilterInput(inputElement, excludedFields, include: false),
                _ => JsonNode.Parse(inputElement.GetRawText()) as JsonObject ?? new JsonObject()
            };
        }
    }
}
