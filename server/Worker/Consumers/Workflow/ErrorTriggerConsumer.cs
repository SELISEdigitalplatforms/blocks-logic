using Blocks.Genesis;
using Workflow.DomainService.Events;
using Workflow.DomainService.Services;

namespace Worker.Consumers.Workflow
{
    /// <summary>
    /// Consumes ErrorTriggerEvent, sent once per failed attempt of a production run whose workflow has an
    /// enabled alert, and starts the alert's error workflow at its Error Trigger.
    /// </summary>
    public class ErrorTriggerConsumer : IConsumer<ErrorTriggerEvent>
    {
        private readonly ILogger<ErrorTriggerConsumer> _logger;
        private readonly IWorkflowExecutionService _workflowExecutionService;

        public ErrorTriggerConsumer(
            ILogger<ErrorTriggerConsumer> logger,
            IWorkflowExecutionService workflowExecutionService)
        {
            _logger = logger;
            _workflowExecutionService = workflowExecutionService;
        }

        public async Task Consume(ErrorTriggerEvent @event)
        {
            _logger.LogInformation("ErrorTriggerConsumer: Received failure of execution {FailedExecutionId}", @event.FailedExecutionId);

            await _workflowExecutionService.ErrorTriggerStartAsync(@event);
        }
    }
}
