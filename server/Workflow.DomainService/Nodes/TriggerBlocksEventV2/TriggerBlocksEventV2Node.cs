using Workflow.DomainService.Logging;
using Workflow.DomainService.Utils;
using System.Text.Json;
using MongoDB.Bson;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Nodes.TriggerBlocksEventV2
{
    /// <summary>
    /// Trigger node executor for Blocks platform events.
    /// Fires when Genesis forwards an enabled catalog event (BlocksEventTriggerMessage) for the tenant;
    /// outputs the one event item the trigger consumer put in the execution's Input.
    /// </summary>
    public class TriggerBlocksEventV2Node : NodeExecutorBase<TriggerBlocksEventV2Parameters>, INodeExecutor
    {
        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };

        public override string NodeType => LogicConstants.BlocksEventNodeType;
        public override string Version => "v2";

        /// <summary>A new type: it exists only as v2 (it owns its handle from the start); there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        protected override Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context,
            TriggerBlocksEventV2Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new TriggerBlocksEventV2Parameters();
            try
            {
                var inputItems = context.WorkflowContext["Input"].AsBsonArray;
                context.Log.Info(ExecutionLogStages.TriggerRead, "Read {Count} item(s) from the trigger event.", inputItems.Count);

                var outputItems = inputItems.Select(item => new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = item.ToBsonDocument(),
                        Output = item.ToBsonDocument(),
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = "source",
                    ParentItemIds = null
                }).ToList();

                return Task.FromResult(NodeExecutionResult.Successful(outputItems));
            }
            catch (Exception ex)
            {
                var errorItem = TryBuildErrorOutputItem(null, parameters.ToBsonDocument(), ex);
                var outputItems = errorItem != null ? new List<NodeOutputItem> { errorItem } : new List<NodeOutputItem>();
                return Task.FromResult(NodeExecutionResult.Failed(ex.Message, outputItems));
            }
        }

        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<TriggerBlocksEventV2Parameters>(
                    parameters, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return Task.FromResult(config != null && !string.IsNullOrWhiteSpace(config.EventName));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}
