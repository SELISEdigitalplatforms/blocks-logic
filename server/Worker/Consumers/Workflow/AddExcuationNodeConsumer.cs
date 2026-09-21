using Blocks.Genesis;
using Workflow.DomainService.Events;
using Workflow.DomainService.Services;


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
