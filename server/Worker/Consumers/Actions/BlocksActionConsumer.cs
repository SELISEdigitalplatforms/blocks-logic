using Blocks.Genesis;
using Functions.DomainService.Services;
using Workflow.DomainService.Actions;

namespace Worker.Consumers.Actions
{
    /// <summary>
    /// Consumes <see cref="BlocksAction"/> from both logic action queues (<see cref="ActionConstants.WorkflowQueue"/> and
    /// <see cref="ActionConstants.FunctionQueue"/>). Genesis routes a message to its consumer by type name only and does
    /// not say which queue it came from, so one host can have only one <see cref="BlocksAction"/> consumer: this one runs
    /// both targets, each of which re-reads its own switch and skips what an earlier delivery of the same event id already
    /// started. The tenant is the message's (<see cref="BlocksContext"/>), never the data's.
    /// </summary>
    public class BlocksActionConsumer : IConsumer<BlocksAction>
    {
        private readonly IActionWorkflowTriggerService _workflows;
        private readonly IFunctionActionTriggerService _functions;
        private readonly ILogger<BlocksActionConsumer> _logger;

        public BlocksActionConsumer(
            IActionWorkflowTriggerService workflows,
            IFunctionActionTriggerService functions,
            ILogger<BlocksActionConsumer> logger)
        {
            _workflows = workflows;
            _functions = functions;
            _logger = logger;
        }

        public async Task Consume(BlocksAction context)
        {
            ArgumentNullException.ThrowIfNull(context);
            _logger.LogInformation("BlocksActionConsumer: received action {Action} ({EventId})", context.Action, context.Id);

            try
            {
                await _workflows.StartAsync(context);
            }
            catch (Exception ex)
            {
                // The functions still run when the workflow side fails.
                _logger.LogError(ex, "Action {Action} ({EventId}) failed for workflows", context.Action, context.Id);
            }

            await _functions.StartAsync(context);
        }
    }
}
