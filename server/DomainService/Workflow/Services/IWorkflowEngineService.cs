
using DomainService.Workflow.Events;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Services
{

    public interface IWorkflowEngineService
    {
        Task RunNodeAsync(AddExcuationNodeEvent dto);
        Task<WorkflowExecutionModel?> RunNodeInProcessAsync(AddExcuationNodeEvent dto);

        Task<WorkflowExecutionModel?> ExecuteStepNodeAsync(string executionId, string targetNodeId, string? sourceExecutionId = null);
    }
}
