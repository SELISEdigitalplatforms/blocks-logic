
using DomainService.Workflow.Events;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Services
{

    public interface IWorkflowEngineService
    {
        Task RunNodeAsync(AddExcuationNodeEvent dto);
        Task<WorkflowExecutionModel?> RunNodeInProcessAsync(AddExcuationNodeEvent dto);

        Task<WorkflowExecutionModel?> ExecuteStepNodeAsync(string tenantId, string executionId, string triggerNodeId, string targetNodeId, string? sourceExecutionId = null);
        IEnumerable<NodeModel> GetTopologicalAncestorsAndTarget(WorkflowModel workflow, string targetNodeId);
    }
}
