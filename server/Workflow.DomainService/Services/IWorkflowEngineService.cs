
using Workflow.DomainService.Events;
using Workflow.DomainService.Entities;

namespace Workflow.DomainService.Services
{

    public interface IWorkflowEngineService
    {
        Task RunNodeAsync(AddExcuationNodeEvent dto);
        Task<WorkflowExecutionEntity?> RunNodeInProcessAsync(AddExcuationNodeEvent dto);

        Task<WorkflowExecutionEntity?> ExecuteStepNodeAsync(string tenantId, string executionId, string triggerNodeId, string targetNodeId, string? sourceExecutionId = null);
        IEnumerable<NodeEntity> GetTopologicalAncestorsAndTarget(WorkflowEntity workflow, string targetNodeId);
    }
}
