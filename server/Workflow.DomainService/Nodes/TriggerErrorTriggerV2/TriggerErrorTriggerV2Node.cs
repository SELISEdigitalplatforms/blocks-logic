using Workflow.DomainService.Logging;
using Workflow.DomainService.Utils;
using MongoDB.Bson;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Nodes.TriggerErrorTriggerV2
{
    /// <summary>
    /// Trigger node executor for the Error Trigger.
    /// Runs when another workflow's production run fails and that workflow's alert names this workflow;
    /// outputs the one failure item ErrorTriggerStartAsync put in the execution's Input.
    /// </summary>
    public class TriggerErrorTriggerV2Node : NodeExecutorBase<object>, INodeExecutor
    {
        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };

        public override string NodeType => LogicConstants.ErrorTriggerNodeType;
        public override string Version => "v2";

        /// <summary>A new type: it exists only as v2; there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        protected override Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context,
            object? nodeparameters)
        {
            // The Error Trigger has no parameters: the failing workflow chooses it in Settings → Alert.
            var parameters = new BsonDocument();
            try
            {
                var inputItems = context.WorkflowContext["Input"].AsBsonArray;
                context.Log.Info(ExecutionLogStages.TriggerRead, "Read {Count} item(s) from the failed run.", inputItems.Count);

                var outputItems = inputItems.Select(item => new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = item.ToBsonDocument(),
                        Output = item.ToBsonDocument(),
                        Parameters = parameters,
                    },
                    Branch = "source",
                    ParentItemIds = null
                }).ToList();

                return Task.FromResult(NodeExecutionResult.Successful(outputItems));
            }
            catch (Exception ex)
            {
                var errorItem = TryBuildErrorOutputItem(null, parameters, ex);
                var outputItems = errorItem != null ? new List<NodeOutputItem> { errorItem } : new List<NodeOutputItem>();
                return Task.FromResult(NodeExecutionResult.Failed(ex.Message, outputItems));
            }
        }
    }
}
