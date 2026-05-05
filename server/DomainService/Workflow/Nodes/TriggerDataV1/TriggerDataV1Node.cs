using System.Text.Json;
using MongoDB.Bson;

namespace DomainService.Workflow.Nodes.TriggerDataV1
{
    /// <summary>
    /// Trigger node executor for Data Gateway changes.
    /// Fires when data is inserted, updated, or deleted in a monitored collection.
    /// </summary>
    public class TriggerDataV1Node : NodeExecutorBase<TriggerDataV1Parameters>
    {
        public override string NodeType => "dataGateway";
        public override string Version => "v1";

        protected override async Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context,
            TriggerDataV1Parameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new TriggerDataV1Parameters();
                var inputItems = context.WorkflowContext["Input"].AsBsonArray;

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

                return NodeExecutionResult.Successful(outputItems);
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<TriggerDataV1Parameters>(parameters);
                return Task.FromResult(config != null &&
                    !string.IsNullOrEmpty(config.CollectionName) &&
                    !string.IsNullOrEmpty(config.Operation));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}
