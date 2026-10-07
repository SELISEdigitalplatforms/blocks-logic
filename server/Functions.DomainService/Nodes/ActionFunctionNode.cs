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

            // Resumed after a failure: items that already succeeded on the failed attempt are taken from it,
            // not run again — there is no rollback, so running them again would repeat their side effects.
            var alreadyDone = AlreadyDone(context);

            for (var i = 0; i < iterations; i++)
            {
                var inputItem = standalone ? StandaloneInputItem(context) : context.InputItems[i];
                if (!standalone && alreadyDone.TryGetValue(inputItem.Id, out var done))
                {
                    outputItems.Add(new NodeOutputItem
                    {
                        Data = done.Data,
                        Branch = done.Branch,
                        ParentItemIds = [inputItem.Id],
                    });
                    continue;
                }

                var inputJson = BuildInputJson(parameters, inputItem, context);

                InvokeResultDto result;
                try
                {
                    // null, always: the step waits exactly as long as the run may take. A stored
                    // WaitTimeoutSec from when this was editable is deliberately not read — see
                    // ActionFunctionParameters.WaitTimeoutSec for why a step must not disagree with
                    // the run it is waiting for.
                    result = await _invocationService.InvokeFromWorkflowAsync(
                        context.TenantId, parameters.FunctionId, inputJson, callerContext,
                        waitTimeoutSeconds: null, context.WorkflowExecutionId, context.CancellationToken);
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
                    // The items before this one really ran (side effects included), so they are kept
                    // on the failed step rather than dropped: the execution must show what was done.
                    return NodeExecutionResult.Failed(ex.Message, outputItems);
                }

                if (!string.Equals(result.Status, FunctionQueueKeys.Wire.Succeeded, StringComparison.Ordinal))
                {
                    return NodeExecutionResult.Failed(FailureMessage(result, i, iterations), outputItems);
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

        /// <summary>The items of the failed attempt, by the input item each was made from.</summary>
        internal static Dictionary<string, WorkflowItemExecutionEntity> AlreadyDone(NodeExecutionContext context)
        {
            var done = new Dictionary<string, WorkflowItemExecutionEntity>(StringComparer.Ordinal);
            foreach (var item in context.PreviousAttemptItems)
            {
                if (item.ParentItemIds is [var parent] && !string.IsNullOrEmpty(parent)) done.TryAdd(parent, item);
            }
            return done;
        }

        /// <summary>
        /// Why the step stopped at item <paramref name="index"/>. A run the step stopped waiting for is
        /// not called failed: it may still finish, or (output processing) has already done its work,
        /// so running the workflow again could do that work twice. The text says so.
        /// </summary>
        internal static string FailureMessage(InvokeResultDto result, int index, int iterations)
        {
            var item = iterations > 1 ? $" (item {index + 1} of {iterations}; items before it succeeded)" : string.Empty;
            if (IsUnfinished(result.Status))
            {
                return $"function run {result.RunId} did not finish in time{item}: it was still {result.Status}. "
                    + "It may still finish or may already have done its work. Check the run before running this workflow again.";
            }

            var reason = result.ErrorMessage ?? result.ErrorCode ?? result.Status;
            return $"function run {result.RunId} did not succeed{item}: {reason}";
        }

        private static bool IsUnfinished(string? status) => status is
            FunctionQueueKeys.Wire.Queued or FunctionQueueKeys.Wire.Claimed or FunctionQueueKeys.Wire.Starting
            or FunctionQueueKeys.Wire.Running or FunctionQueueKeys.Wire.OutputProcessing;

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
                return resolved switch
                {
                    null => null,
                    // The shared expression parser hands back Newtonsoft tokens for JSON (JObject,
                    // JArray, JValue). System.Text.Json does not know them and wrote every object as
                    // nested empty arrays: {"a":1} reached the function as {"a":[]} (WF-11, 2026-10-06).
                    Newtonsoft.Json.Linq.JToken token => token.ToString(Newtonsoft.Json.Formatting.None),
                    _ => JsonSerializer.Serialize(resolved),
                };
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
