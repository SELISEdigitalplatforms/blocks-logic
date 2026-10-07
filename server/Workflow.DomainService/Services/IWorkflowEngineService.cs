
using Workflow.DomainService.Events;
using Workflow.DomainService.Entities;

namespace Workflow.DomainService.Services
{

    public interface IWorkflowEngineService
    {
        /// <param name="stopping">The Worker is shutting down: a step still waiting stops and is marked interrupted.</param>
        Task RunNodeAsync(AddExcuationNodeEvent dto, CancellationToken stopping = default);
        Task<WorkflowExecutionEntity?> RunNodeInProcessAsync(AddExcuationNodeEvent dto);

        Task<WorkflowExecutionEntity?> ExecuteStepNodeAsync(string tenantId, string executionId, string triggerNodeId, string targetNodeId, string? sourceExecutionId = null);
        IEnumerable<NodeEntity> GetTopologicalAncestorsAndTarget(WorkflowEntity workflow, string targetNodeId);
    }
}
