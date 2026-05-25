using Blocks.Genesis;
using DomainService.Workflow.Events;
using DomainService.Workflow.Services;


namespace Worker.Consumers.Workflow
{
    public class AddExcuationNodeConsumer : IConsumer<AddExcuationNodeEvent>
    {
        private readonly IWorkflowEngineService _workflowEngineService;
        public AddExcuationNodeConsumer(IWorkflowEngineService workflowEngineService)
        {
            _workflowEngineService = workflowEngineService;
        }

        public async Task Consume(AddExcuationNodeEvent @event)
        {
            await _workflowEngineService.RunNodeAsync(@event);
        }
    }
}
