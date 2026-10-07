using Microsoft.Extensions.Hosting;
using Blocks.Genesis;
using Workflow.DomainService.Events;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;


namespace Worker.Consumers.Workflow
{
    public class AddExcuationNodeConsumer : IConsumer<AddExcuationNodeEvent>
    {
        private readonly IWorkflowEngineService _workflowEngineService;
        private readonly NodeQueueLanes _lanes;
        private readonly IHostApplicationLifetime _lifetime;

        public AddExcuationNodeConsumer(IWorkflowEngineService workflowEngineService, NodeQueueLanes lanes, IHostApplicationLifetime lifetime)
        {
            _workflowEngineService = workflowEngineService;
            _lanes = lanes;
            _lifetime = lifetime;
        }

        public async Task Consume(AddExcuationNodeEvent @event)
        {
            // One step at a time per run's queue, and a tenant within its share (NodeQueueLanes).
            await _lanes.RunAsync(@event.TenantId, @event.WorkflowExecutionId,
                () => _workflowEngineService.RunNodeAsync(@event, _lifetime.ApplicationStopping));
        }
    }
}
