using System.Text.Json;
using Blocks.Genesis;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Utils;
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
    /// <see cref="Workflow.DomainService.Services.IWorkflowAuthService"/>), so registering this
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

            // Checked here rather than left to the invocation service: a non-positive wait window
            // makes WaitForResultAsync's deadline expire before its first poll, so the step would
            // queue a run that really does execute and then fail as "still running" — a side effect
            // with no result, from a value the editor should not have accepted in the first place.
            if (parameters.WaitTimeoutSec is <= 0)
            {
                return NodeExecutionResult.Failed(
                    $"wait timeout must be at least 1 second, but is {parameters.WaitTimeoutSec}");
            }

            var callerContext = BlocksContext.GetContext();
            var outputItems = new List<NodeOutputItem>();

            // In "expression" input mode the step is self-contained, and even in "item" mode the
            // function may take no input at all, so a lone function node has something to run. With
            // nothing wired to it there is no producer to take items from, and iterating zero times
            // would make a single-node test of it silently succeed without ever calling the function.
            //
            // A node that DOES have an upstream keeps the zero-iteration behaviour exactly. An empty
            // input there means an upstream branch that was not taken (the engine dispatches down every
            // outgoing edge and relies on the zero-item node to prune), and firing anyway would run the
            // tenant's function — and its output actions — on a path the workflow deliberately skipped.
            var standalone = context.IterationCount == 0 && !context.HasUpstream;
            var iterations = standalone ? 1 : context.IterationCount;

            for (var i = 0; i < iterations; i++)
            {
                var inputItem = standalone ? StandaloneInputItem(context) : context.InputItems[i];
                var inputJson = BuildInputJson(parameters, inputItem, context);

                InvokeResultDto result;
                try
                {
                    result = await _invocationService.InvokeFromWorkflowAsync(
                        context.TenantId, parameters.FunctionId, inputJson, callerContext,
                        parameters.WaitTimeoutSec, context.WorkflowExecutionId, context.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the engine stopping this execution, not the function failing,
                    // so it is left to the engine rather than reported as the function's own error
                    // — the same split ActionProxyNode makes. The step still ends up failed either
                    // way; what differs is only that the recorded reason is the cancellation.
                    throw;
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
                    // A standalone run has no parent item, so it claims none: the engine reads these
                    // ids back out of InputItems to build the ancestor map.
                    ParentItemIds = standalone ? [] : [inputItem.Id],
                });
            }

            return NodeExecutionResult.Successful(outputItems);
        }

        /// <summary>
        /// The stand-in input item for a function node with nothing wired to it. Carries an empty
        /// payload, so "item" mode sends no input and an expression resolving against
        /// <c>$json</c> resolves to nothing rather than throwing.
        /// </summary>
        private static WorkflowItemExecutionEntity StandaloneInputItem(NodeExecutionContext context) => new()
        {
            Id = string.Empty,
            WorkflowExecutionId = context.WorkflowExecutionId,
            TenantId = context.TenantId,
            NodeId = context.NodeId,
            NodeExecutionId = string.Empty,
            NodeName = string.Empty,
            Branch = "source",
            Data = new NodeOutputItemData(),
        };

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
