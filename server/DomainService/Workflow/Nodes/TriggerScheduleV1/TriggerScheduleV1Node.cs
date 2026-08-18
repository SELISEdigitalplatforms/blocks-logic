using System.Text.Json;
using MongoDB.Bson;
using Scheduler.DomainService.Utils;

namespace DomainService.Workflow.Nodes.TriggerScheduleV1
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
            try
            {
                var parameters = nodeparameters ?? new TriggerScheduleV1Parameters();
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
