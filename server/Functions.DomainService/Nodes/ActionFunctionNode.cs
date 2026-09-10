using System.Text.Json;
using Blocks.Genesis;
using DomainService.Workflow.Entities;
using DomainService.Workflow.Nodes;
using DomainService.Workflow.Utils;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Queue;
using Functions.DomainService.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Functions.DomainService.Nodes
{
    /// <summary>
    /// The workflow action step that invokes a deployed function synchronously
    /// (<c>NodeType "function"</c>). Lives in <c>Functions.DomainService</c>, not
    /// <c>DomainService</c>, and is deliberately registered as <c>INodeExecutor</c> from the
    /// Api/Worker composition root rather than from
    /// <c>WorkflowExecutionServiceCollectionExtensions.AddWorkflowExecutionEngine()</c>:
    /// <c>Functions.DomainService</c> already depends on <c>DomainService</c> (for
    /// <see cref="DomainService.Workflow.Services.IWorkflowAuthService"/>), so registering this
    /// node from inside <c>DomainService</c> itself would create a circular project reference.
    /// </summary>
    public class ActionFunctionNode : NodeExecutorBase<ActionFunctionParameters>
    {
        public override string NodeType => "function";
        public override string Version => "v1";

        private readonly IFunctionInvocationService _invocationService;
        private readonly ILogger<ActionFunctionNode> _logger;

        public ActionFunctionNode(IFunctionInvocationService invocationService, ILogger<ActionFunctionNode> logger)
        {
            _invocationService = invocationService;
            _logger = logger;
        }

        protected override async Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context, ActionFunctionParameters? nodeParameters)
        {
            var parameters = nodeParameters ?? new ActionFunctionParameters();
            if (string.IsNullOrWhiteSpace(parameters.FunctionId))
            {
                return NodeExecutionResult.Failed("the function action step has no function selected");
            }

            var callerContext = BlocksContext.GetContext();
            var outputItems = new List<NodeOutputItem>();

            for (var i = 0; i < context.IterationCount; i++)
            {
                var inputItem = context.InputItems[i];
                var inputJson = BuildInputJson(parameters, inputItem, context);

                InvokeResultDto result;
                try
                {
                    result = await _invocationService.InvokeFromWorkflowAsync(
                        context.TenantId, parameters.FunctionId, inputJson, callerContext,
                        parameters.WaitTimeoutSec, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Function step failed to invoke function {FunctionId}", parameters.FunctionId);
                    return NodeExecutionResult.Failed(ex.Message);
                }

                if (!string.Equals(result.Status, FunctionQueueKeys.Wire.Succeeded, StringComparison.Ordinal))
                {
                    var reason = result.ErrorMessage ?? result.ErrorCode ?? result.Status;
                    return NodeExecutionResult.Failed($"function run {result.RunId} did not succeed: {reason}");
                }

                outputItems.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = inputItem.Data.Output,
                        Output = ParseResult(result.Result),
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = "source",
                    ParentItemIds = [inputItem.Id],
                });
            }

            return NodeExecutionResult.Successful(outputItems);
        }

        /// <summary>
        /// "item" passes the current input item's own output through as the function's input,
        /// unchanged; "expression" evaluates <see cref="ActionFunctionParameters.InputExpression"/>
        /// the same way every other node's expression fields are evaluated.
        /// </summary>
        private string? BuildInputJson(
            ActionFunctionParameters parameters, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            if (string.Equals(parameters.InputMode, "expression", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = parseExpression<object>(parameters.InputExpression, inputItem, context);
                return resolved is null ? null : JsonSerializer.Serialize(resolved);
            }

            return inputItem.Data.Output is null
                ? null
                : inputItem.Data.Output.ToJson(new MongoDB.Bson.IO.JsonWriterSettings
                {
                    OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson,
                });
        }

        private static BsonValue ParseResult(string? resultJson)
        {
            if (string.IsNullOrEmpty(resultJson)) return BsonNull.Value;
            try
            {
                return BsonJsonConverter.ToBsonValue(JsonDocument.Parse(resultJson).RootElement);
            }
            catch (JsonException)
            {
                // The result is whatever the tenant's own function returned; if for some
                // reason it is not valid JSON, surface it as a string rather than failing the
                // whole step over an output-formatting concern.
                return resultJson;
            }
        }
    }
}
