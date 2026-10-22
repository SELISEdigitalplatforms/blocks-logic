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
    /// Run When All outputs nothing unless every connected input 1..N has items; Any outputs whatever arrived.
    /// </summary>
    public class TransformMergeV2Node : NodeExecutorBase<TransformMergeV2Parameters>, INodeExecutor
    {
        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };
        private static readonly Regex InputKey = new(@"^merge-handle-target-(\d+)$", RegexOptions.None, TimeSpan.FromSeconds(1));

        public override string NodeType => "merge";

        public override string Version => "v2";

        /// <summary>Merge exists only as v2 (it owns its handle from the start); there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        public const int MinInputs = 2;
        public const int MaxInputs = 10;

        /// <summary>The number of inputs: missing 2, otherwise floored and kept within 2 to 10, as the editor allows.</summary>
        public static int InputCount(double? numberInputs)
        {
            if (numberInputs is not { } value || double.IsNaN(value)) return MinInputs;
            return (int)Math.Clamp(Math.Floor(value), MinInputs, MaxInputs);
        }

        protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TransformMergeV2Parameters? nodeparameters)
        {
            // Append is the only mode; any other saved value already failed the parameter shape.
            var parameters = nodeparameters ?? new TransformMergeV2Parameters();
            var inputCount = InputCount(parameters.NumberInputs);

            // Only the handles that are present are visited.
            var ordered = new List<(int Input, int Blank, IReadOnlyList<WorkflowItemExecutionEntity> Items)>();
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
                    && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var input)
                    && input >= 1 && input <= inputCount)
                {
                    ordered.Add((input, 1, items));
                }
                else
                {
                    ignored += items.Count;
                }
            }

            // Every connected input has an entry, empty when its parent sent nothing (or never ran).
            if (parameters.RunWhen == RunWhenMode.All)
            {
                var countByInput = ordered.GroupBy(entry => entry.Input).Select(group => group.Sum(entry => entry.Items.Count)).ToList();
                var empty = countByInput.Count(count => count == 0);
                if (empty > 0)
                {
                    context.Log.Info(ExecutionLogStages.MergeRunWhenUnmet, "Run When is All: {Empty} of {Inputs} input(s) had no items; nothing was output.",
                        empty, countByInput.Count);
                    return Task.FromResult(NodeExecutionResult.Successful([]));
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
                            // As saved: the enum would otherwise be stored as a number.
                            Parameters = context.Parameters,
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
