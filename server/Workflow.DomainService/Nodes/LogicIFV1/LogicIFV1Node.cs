using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes.Conditions;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Nodes.LogicIFV1
{
    [ExcludeFromCodeCoverage]
    public class LogicIfV1Node : NodeExecutorBase<LogicIfV1Parameter>
    {
        public override string NodeType => "if";

        public override string Version => "v1";

        protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, LogicIfV1Parameter? nodeparameters)
        {
            var parameters = nodeparameters ?? new LogicIfV1Parameter();
            var outputItems = new List<NodeOutputItem>();
            var conditionType = parameters.ConditionType?.Trim().ToLowerInvariant();
            var useOr = conditionType == "or";

            // An item whose conditions cannot be evaluated (unknown operator or type, an expression error) used to
            // become an error item on branch "source". An If node has no "source" edge, so the item was dropped
            // without a trace and the run still "succeeded". Now the step fails and names the first bad item.
            string? firstError = null;
            var failedCount = 0;

            for (var i = 0; i < context.IterationCount; i++)
            {
                try
                {
                    var inputItem = context.InputItems[i];
                    // Every condition is evaluated (no short-circuit), so a bad operator fails the item every
                    // time instead of only when the earlier conditions happen not to decide the result.
                    var results = parameters.Conditions.Select(condition => EvaluateCondition(condition, inputItem, context)).ToList();
                    bool conditionMet = useOr ? results.Any(r => r) : results.All(r => r);

                    var branch = conditionMet ? "if-true" : "if-false";
                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData
                        {
                            Parameters = parameters.ToBsonDocument(),
                            Input = inputItem.Data.Input,
                            Output = inputItem.Data.Output,
                        },
                        Branch = branch,
                        ParentItemIds = new List<string>() { inputItem.Id },
                    });
                }
                catch (Exception ex)
                {
                    failedCount++;
                    firstError ??= $"Item {i}: {ex.Message}";
                    AppendErrorOutputItem(outputItems, context.InputItems[i], parameters.ToBsonDocument(), ex);
                }
            }
            context.Log.Info(ExecutionLogStages.IfEvaluated, "Evaluated {Count} item(s): {True} true, {False} false.",
                context.IterationCount,
                outputItems.Count(o => o.Branch == "if-true"),
                outputItems.Count(o => o.Branch == "if-false"));

            if (firstError != null)
            {
                var message = failedCount == 1
                    ? $"Condition could not be evaluated. {firstError}"
                    : $"Condition could not be evaluated for {failedCount} item(s). First: {firstError}";
                return Task.FromResult(NodeExecutionResult.Failed(message, outputItems));
            }
            return Task.FromResult(NodeExecutionResult.Successful(outputItems));
        }

        private bool EvaluateCondition(Condition condition, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
            => ConditionEvaluator.Evaluate(condition, text => parseExpression<string>(text, inputItem, context), ignoreCase: false);
    }
}