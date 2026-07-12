
using DomainService.Workflow.Events;
using DomainService.Workflow.Entities;

namespace DomainService.Workflow.Services
{

    public interface IWorkflowEngineService
    {
        Task RunNodeAsync(AddExcuationNodeEvent dto);
        Task<WorkflowExecutionEntity?> RunNodeInProcessAsync(AddExcuationNodeEvent dto);

        Task<WorkflowExecutionEntity?> ExecuteStepNodeAsync(string tenantId, string executionId, string triggerNodeId, string targetNodeId, string? sourceExecutionId = null);
        IEnumerable<NodeEntity> GetTopologicalAncestorsAndTarget(WorkflowEntity workflow, string targetNodeId);
    }
}
