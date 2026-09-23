using System.Text.Json;
using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;

namespace Workflow.DomainService.Nodes.TriggerWebhookV1
{
    [ExcludeFromCodeCoverage]
    public class TriggerWebhookV1Node : NodeExecutorBase<TriggerWebhookV1Parameters>
    {
        public override string NodeType => "webhook";
        public override string Version => "1.0";


        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TriggerWebhookV1Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new TriggerWebhookV1Parameters();
            try
            {
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
                }
                ).ToList();
                return NodeExecutionResult.Successful(outputItems);
            }
            catch (Exception ex)
            {
                var errorItem = TryBuildErrorOutputItem(null, parameters.ToBsonDocument(), ex);
                var outputItems = errorItem != null ? new List<NodeOutputItem> { errorItem } : new List<NodeOutputItem>();
                return NodeExecutionResult.Failed(ex.Message, outputItems);
            }
        }

        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<TriggerWebhookV1Parameters>(parameters);
                return Task.FromResult(config != null);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}