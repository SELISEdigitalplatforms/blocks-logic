using MongoDB.Bson;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Utils;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Nodes.TransformSplitOutV2
{
    /// <summary>
    /// Split Out: turns a list inside each item into separate items (n8n Split Out). Several fields are
    /// combined by position. Each new item keeps its input item as its parent, so $node["…"] expressions
    /// downstream still resolve to the item it came from.
    /// </summary>
    public class TransformSplitOutV2Node : NodeExecutorBase<TransformSplitOutV2Parameters>, INodeExecutor
    {
        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };

        public override string NodeType => "splitOut";

        public override string Version => "v2";

        /// <summary>Split Out exists only as v2; there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        private static List<string> ParseList(string? text, bool stripJsonPrefix) =>
            (text ?? string.Empty)
                .Split(',')
                .Select(part => part.Trim())
                .Select(part => stripJsonPrefix ? StripJsonPrefix(part) : part)
                .Where(part => part.Length > 0)
                .ToList();

        private static string StripJsonPrefix(string path)
        {
            if (path.StartsWith("$json.output.", StringComparison.Ordinal)) return path["$json.output.".Length..];
            if (path.StartsWith("$json.", StringComparison.Ordinal)) return path["$json.".Length..];
            return path;
        }

        protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TransformSplitOutV2Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new TransformSplitOutV2Parameters();
            var dotNotation = !parameters.DisableDotNotation;
            var include = parameters.Include?.Trim() switch
            {
                "allOtherFields" => "allOtherFields",
                "selectedOtherFields" => "selectedOtherFields",
                _ => "noOtherFields",
            };

            var fields = ParseList(parameters.FieldToSplitOut, stripJsonPrefix: true);
            if (fields.Count == 0)
                return Task.FromResult(NodeExecutionResult.Failed("Fields To Split Out is required."));

            var fieldsToInclude = ParseList(parameters.FieldsToInclude, stripJsonPrefix: false);
            if (include == "selectedOtherFields" && fieldsToInclude.Count == 0)
                return Task.FromResult(NodeExecutionResult.Failed("Fields To Include is required."));

            var destinations = ParseList(parameters.DestinationFieldName, stripJsonPrefix: false);
            if (destinations.Count > 0 && destinations.Count != fields.Count)
                return Task.FromResult(NodeExecutionResult.Failed("Destination Field Name must list one name per field to split out."));

            var outputItems = new List<NodeOutputItem>();
            var foundSomewhere = new bool[fields.Count];

            for (var i = 0; i < context.IterationCount; i++)
            {
                var inputItem = context.InputItems[i];
                try
                {
                    var doc = inputItem.Data.Output as BsonDocument;
                    foreach (var entry in SplitItem(doc, fields, destinations, include, fieldsToInclude, dotNotation, foundSomewhere))
                    {
                        outputItems.Add(new NodeOutputItem
                        {
                            Data = new NodeOutputItemData
                            {
                                Input = inputItem.Data.Output,
                                Output = entry,
                                Parameters = parameters.ToBsonDocument(),
                            },
                            Branch = "source",
                            ParentItemIds = new List<string> { inputItem.Id },
                        });
                    }
                }
                catch (Exception ex)
                {
                    AppendErrorOutputItem(outputItems, inputItem, parameters.ToBsonDocument(), ex);
                }
            }

            // Counts only: never field names, parameter values or item data.
            context.Log.Info(ExecutionLogStages.SplitOutApplied, "Split {Inputs} item(s) into {Count} item(s).",
                context.IterationCount, outputItems.Count(o => !IsErrorItem(o)));
            var notFound = context.IterationCount == 0 ? 0 : foundSomewhere.Count(found => !found);
            if (notFound > 0)
                context.Log.Info(ExecutionLogStages.SplitOutFieldNotFound, "{Count} field(s) were not found in any input item.", notFound);

            return Task.FromResult(NodeExecutionResult.Successful(outputItems));
        }

        private static List<BsonDocument> SplitItem(
            BsonDocument? doc,
            List<string> fields,
            List<string> destinations,
            string include,
            List<string> fieldsToInclude,
            bool dotNotation,
            bool[] foundSomewhere)
        {
            var entries = new List<BsonDocument>();
            var multiSplit = fields.Count > 1;

            for (var k = 0; k < fields.Count; k++)
            {
                var field = fields[k];
                var destination = destinations.Count > 0 ? destinations[k] : string.Empty;
                var name = destination.Length > 0 ? destination : field;

                List<BsonValue> elements;
                if (doc is null || !BsonPath.TryGet(doc, field, dotNotation, out var value))
                {
                    elements = new List<BsonValue>();
                }
                else
                {
                    foundSomewhere[k] = true;
                    elements = value switch
                    {
                        BsonArray array => array.ToList(),
                        BsonDocument nested => nested.Values.ToList(),
                        _ => new List<BsonValue> { value },
                    };
                }

                for (var j = 0; j < elements.Count; j++)
                {
                    if (entries.Count <= j) entries.Add(new BsonDocument());
                    var element = elements[j];
                    if (element is BsonDocument elementDoc && include == "noOtherFields" && destination.Length == 0 && !multiSplit)
                    {
                        foreach (var property in elementDoc) entries[j][property.Name] = property.Value.DeepClone();
                    }
                    else
                    {
                        entries[j][name] = element.DeepClone();
                    }
                }
            }

            if (doc is null) return entries;

            for (var j = 0; j < entries.Count; j++)
            {
                if (include == "allOtherFields")
                {
                    var merged = (BsonDocument)doc.DeepClone();
                    foreach (var field in fields) BsonPath.Remove(merged, field, dotNotation);
                    foreach (var property in entries[j]) merged[property.Name] = property.Value;
                    entries[j] = merged;
                }
                else if (include == "selectedOtherFields")
                {
                    foreach (var path in fieldsToInclude)
                    {
                        if (BsonPath.TryGet(doc, path, dotNotation, out var included))
                            entries[j][path] = included.DeepClone();
                    }
                }
            }
            return entries;
        }
    }
}
