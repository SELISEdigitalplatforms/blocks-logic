using Workflow.DomainService.Logging;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Nodes.TransformLimitV2
{
    /// <summary>
    /// Limit: keeps at most Max Items of the input items, from the start or the end of the list (n8n Limit).
    /// Runs once over all items. Kept items leave unchanged and in input order, each with its input item as
    /// its parent, so $node["…"] expressions downstream still resolve to the item it came from.
    /// </summary>
    public class TransformLimitV2Node : NodeExecutorBase<TransformLimitV2Parameters>, INodeExecutor
    {
        public override string NodeType => "limit";

        public override string Version => "v2";

        protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TransformLimitV2Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new TransformLimitV2Parameters();

            // StringEnumConverter accepts any number without a range check, so 2 would bind here.
            if (!Enum.IsDefined(parameters.Keep))
                return Task.FromResult(NodeExecutionResult.Failed(NodeParameterShape.Fallback("keep")));

            if (parameters.MaxItems < 1)
                return Task.FromResult(NodeExecutionResult.Failed("Max Items must be 1 or more."));

            var inputs = context.InputItems;
            var kept = Math.Min(parameters.MaxItems, inputs.Count);
            var start = parameters.Keep == LimitKeep.Last ? inputs.Count - kept : 0;

            var outputItems = inputs.Skip(start).Take(kept).Select(inputItem => new NodeOutputItem
            {
                Data = new NodeOutputItemData
                {
                    Input = inputItem.Data.Input,
                    Output = inputItem.Data.Output,
                    Parameters = context.Parameters,
                },
                Branch = "source",
                ParentItemIds = new List<string> { inputItem.Id },
            }).ToList();

            context.Log.Info(ExecutionLogStages.LimitApplied, "Kept {Kept} of {Total} item(s).", kept, inputs.Count);

            return Task.FromResult(NodeExecutionResult.Successful(outputItems));
        }
    }
}
