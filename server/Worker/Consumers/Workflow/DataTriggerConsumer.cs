using Blocks.Genesis;
using DomainService.Workflow.Nodes.TriggerDataV1;
using DomainService.Workflow.Services;

namespace Worker.Consumers.Workflow
{
    /// <summary>
    /// Consumes DataChangeEvent from blocks-uds-net when data is inserted, updated, or deleted.
    /// Triggers matching workflows via WorkflowExecutionService.
    /// </summary>
    public class DataTriggerConsumer : IConsumer<DataChangeEvent>
    {
        private readonly ILogger<DataTriggerConsumer> _logger;
        private readonly IWorkflowExecutionService _workflowExecutionService;

        public DataTriggerConsumer(
            ILogger<DataTriggerConsumer> logger,
            IWorkflowExecutionService workflowExecutionService)
        {
            _logger = logger;
            _workflowExecutionService = workflowExecutionService;
        }

        public async Task Consume(DataChangeEvent @event)
        {
            _logger.LogInformation("DataTriggerConsumer: Received {Operation} event for {CollectionName}",
                @event.Operation, @event.CollectionName);

            await _workflowExecutionService.DataTriggerStartAsync(@event);
        }
    }
}
