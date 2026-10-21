using System.Globalization;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using Workflow.DomainService.Logging;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Nodes.TransformMergeV2
{
    /// <summary>
    /// Merge (Append): outputs the items of input 1, then input 2, … input N, each item a copy of its source
    /// item with that item as its parent. Inputs are the target handles merge-handle-target-1..N; a blank
    /// target handle counts as input 1. Items on other handles are ignored and counted.
    /// </summary>
    public class TransformMergeV2Node : NodeExecutorBase<TransformMergeV2Parameters>, INodeExecutor
    {
        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };
        private static readonly Regex InputKey = new(@"^merge-handle-target-(\d+)$", RegexOptions.None, TimeSpan.FromSeconds(1));

        public override string NodeType => "merge";

        public override string Version => "v2";

        /// <summary>Merge exists only as v2 (it owns its handle from the start); there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        /// <summary>The number of inputs: missing or null 2, below 1 1, otherwise floored (no upper limit).</summary>
        public static double InputCount(double? numberInputs)
        {
            if (numberInputs is not { } value || double.IsNaN(value)) return 2;
            return value < 1 ? 1 : Math.Floor(value);
        }

        protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TransformMergeV2Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new TransformMergeV2Parameters();
            if (!string.Equals((parameters.Mode ?? "append").Trim(), "append", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(NodeExecutionResult.Failed("Unsupported Merge mode."));

            var inputCount = InputCount(parameters.NumberInputs);

            // Only the handles that are present are visited (bounded by the edges, not by the input count).
            var ordered = new List<(double Input, int Blank, IReadOnlyList<WorkflowItemExecutionEntity> Items)>();
            var ignored = 0;
            foreach (var (key, items) in context.InputItemsByHandle)
            {
                if (key.Length == 0)
                {
                    ordered.Add((1, 0, items));
                    continue;
                }
                var match = InputKey.Match(key);
                if (match.Success
                    && double.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var input)
                    && input >= 1 && input <= inputCount)
                {
                    ordered.Add((input, 1, items));
                }
                else
                {
                    ignored += items.Count;
                }
            }

            var outputItems = new List<NodeOutputItem>();
            var inputsUsed = 0;
            foreach (var (_, _, items) in ordered.OrderBy(entry => entry.Input).ThenBy(entry => entry.Blank))
            {
                if (items.Count > 0) inputsUsed++;
                foreach (var source in items)
                {
                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData
                        {
                            Input = source.Data.Output,
                            Output = source.Data.Output?.DeepClone() ?? BsonNull.Value,
                            Parameters = parameters.ToBsonDocument(),
                        },
                        Branch = "source",
                        ParentItemIds = new List<string> { source.Id },
                    });
                }
            }

            // Counts only: never handle names, settings or item data.
            context.Log.Info(ExecutionLogStages.MergeApplied, "Appended {Count} item(s) from {Inputs} input(s).", outputItems.Count, inputsUsed);
            if (ignored > 0)
            {
                context.Log.Info(ExecutionLogStages.MergeIgnored, "Ignored {Count} item(s) from inputs above {N} or with unknown names.",
                    ignored, inputCount);
            }

            return Task.FromResult(NodeExecutionResult.Successful(outputItems));
        }
    }
}
