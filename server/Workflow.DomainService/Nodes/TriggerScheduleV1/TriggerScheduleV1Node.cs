using System.Text.Json;
using MongoDB.Bson;
using Scheduler.DomainService.Utils;

namespace Workflow.DomainService.Nodes.TriggerScheduleV1
{
    /// <summary>
    /// Trigger node executor for Schedule triggers.
    /// Fires when the scheduler publishes a workflow schedule trigger event
    /// on the workflow scheduler trigger queue.
    /// </summary>
    public class TriggerScheduleV1Node : NodeExecutorBase<TriggerScheduleV1Parameters>
    {
        public override string NodeType => "schedule";
        public override string Version => "v1";

        protected override async Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context,
            TriggerScheduleV1Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new TriggerScheduleV1Parameters();
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
                }).ToList();

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
                var config = JsonSerializer.Deserialize<TriggerScheduleV1Parameters>(parameters);
                return Task.FromResult(config != null &&
                    !string.IsNullOrEmpty(config.CronExpression) &&
                    Helper.IsValidCronExpression(config.CronExpression));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}
