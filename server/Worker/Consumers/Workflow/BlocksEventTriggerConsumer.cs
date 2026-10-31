using Blocks.Genesis;
using Workflow.DomainService.Services;

namespace Worker.Consumers.Workflow
{
    /// <summary>
    /// Consumes BlocksEventTriggerMessage, which Genesis forwards from any Blocks service when the tenant has
    /// the sent event enabled for workflows. Starts the published workflows that listen to it via
    /// WorkflowExecutionService. The tenant is the message's (BlocksContext), never the payload's.
    /// </summary>
    public class BlocksEventTriggerConsumer : IConsumer<BlocksEventTriggerMessage>
    {
        private readonly ILogger<BlocksEventTriggerConsumer> _logger;
        private readonly IWorkflowExecutionService _workflowExecutionService;

        public BlocksEventTriggerConsumer(
            ILogger<BlocksEventTriggerConsumer> logger,
            IWorkflowExecutionService workflowExecutionService)
        {
            _logger = logger;
            _workflowExecutionService = workflowExecutionService;
        }

        public async Task Consume(BlocksEventTriggerMessage @event)
        {
            _logger.LogInformation("BlocksEventTriggerConsumer: Received {EventName} ({EventId}) from {SourceService}",
                @event.EventName, @event.EventId, @event.SourceService);

            await _workflowExecutionService.BlocksEventTriggerStartAsync(@event);
        }
    }
}
