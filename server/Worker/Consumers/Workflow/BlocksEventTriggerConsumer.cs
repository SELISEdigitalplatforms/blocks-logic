using Blocks.Genesis;
using Workflow.DomainService.BlocksEvents;

namespace Worker.Consumers.Workflow
{
    /// <summary>
    /// Consumes BlocksEventTriggerMessage, which Genesis forwards from any Blocks service when the tenant has
    /// the sent event enabled for workflows. Starts the published workflows that listen to it via
    /// BlocksEventTriggerService. The tenant is the message's (BlocksContext), never the payload's.
    /// </summary>
    public class BlocksEventTriggerConsumer : IConsumer<BlocksEventTriggerMessage>
    {
        private readonly ILogger<BlocksEventTriggerConsumer> _logger;
        private readonly IBlocksEventTriggerService _triggerService;

        public BlocksEventTriggerConsumer(
            ILogger<BlocksEventTriggerConsumer> logger,
            IBlocksEventTriggerService triggerService)
        {
            _logger = logger;
            _triggerService = triggerService;
        }

        public async Task Consume(BlocksEventTriggerMessage @event)
        {
            _logger.LogInformation("BlocksEventTriggerConsumer: Received {EventName} ({EventId}) from {SourceService}",
                @event.EventName, @event.EventId, @event.SourceService);

            await _triggerService.StartAsync(@event);
        }
    }
}
