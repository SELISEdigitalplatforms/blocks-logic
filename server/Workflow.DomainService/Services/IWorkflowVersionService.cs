using Blocks.Genesis;
using Workflow.DomainService.Dtos;

namespace Workflow.DomainService.Services
{
    public interface IWorkflowVersionService
    {
        Task<BaseMutationResponse> CreateVersionAsync(string tenantId, WorkflowVersionCreateRequestDto dto);

        Task<BaseMutationResponse> UpdateVersionAsync(string tenantId, WorkflowVersionUpdateRequestDto dto);

        Task<WorkflowGetVersionsResponseDto> GetWorkflowVersionsAsync(string tenantId, WorkflowGetVersionsRequestDto dto);
    }
}