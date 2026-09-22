using Blocks.Genesis;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Events;

namespace Workflow.DomainService.Services
{
    public interface IWorkflowImportService
    {
        Task<BaseMutationResponse> EnqueueAsync(WorkflowImportRequestDto request);

        Task ImportAsync(WorkflowImportEvent evt);
    }
}
