using System.Text.Json;
using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Nodes.TriggerEmailV1
{
    [ExcludeFromCodeCoverage]
    public class TriggerEmailV1Node : NodeExecutorBase<TriggerEmailV1Parameters>
    {


        public override string NodeType => "email";
        public override string Version => "v1";

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TriggerEmailV1Parameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new TriggerEmailV1Parameters();
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
                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<TriggerEmailV1Parameters>(parameters);
                return Task.FromResult(config != null);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}