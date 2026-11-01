using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes.Conditions;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Nodes.LogicFilterV2
{
    /// <summary>
    /// Filter: keeps the items that match the conditions and drops the rest. Conditions are evaluated by the
    /// same evaluator as If and Switch, so a condition means the same in all three. A kept item is the input
    /// item unchanged, with the input item as its parent, so $node["…"] expressions downstream still resolve.
    /// </summary>
    public class LogicFilterV2Node : NodeExecutorBase<LogicFilterV2Parameters>, INodeExecutor
    {
        public override string NodeType => "filter";

        public override string Version => "v2";

        protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, LogicFilterV2Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new LogicFilterV2Parameters();
            // StringEnumConverter binds any integer, so 2 or -1 arrive here as undefined members.
            if (!Enum.IsDefined(parameters.ConditionType))
                return Task.FromResult(NodeExecutionResult.Failed(NodeParameterShape.Fallback("conditionType")));

            var useOr = parameters.ConditionType == FilterConditionType.Or;
            var outputItems = new List<NodeOutputItem>();
            int failedCount = 0, kept = 0;

            for (var i = 0; i < context.IterationCount; i++)
            {
                var inputItem = context.InputItems[i];
                try
                {
                    // Every condition is evaluated (no short-circuit), as in If: a bad condition fails the item
                    // even when an earlier one already decides the result.
                    var results = parameters.Conditions
                        .Select(condition => ConditionEvaluator.Evaluate(condition, text => parseExpression<string>(text, inputItem, context)))
                        .ToList();
                    if (useOr ? results.Exists(r => r) : results.TrueForAll(r => r))
                    {
                        outputItems.Add(new NodeOutputItem
                        {
                            Data = new NodeOutputItemData
                            {
                                Input = inputItem.Data.Input,
                                Output = inputItem.Data.Output,
                                // As saved (camelCase text values), not re-serialized from the typed parameters.
                                Parameters = context.Parameters,
                            },
                            Branch = "source",
                            ParentItemIds = new List<string> { inputItem.Id },
                        });
                        kept++;
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    AppendErrorOutputItem(outputItems, inputItem, context.Parameters, ex);
                }
            }

            // Counts only: never condition text, operands or item values.
            context.Log.Info(ExecutionLogStages.FilterApplied, "Kept {Kept} of {Total} item(s).",
                kept, context.IterationCount);

            if (failedCount > 0)
                return Task.FromResult(NodeExecutionResult.Failed(
                    $"Condition could not be evaluated for {failedCount} item(s).", outputItems));
            return Task.FromResult(NodeExecutionResult.Successful(outputItems));
        }
    }
}
