using Blocks.Genesis;
using DomainService.Workflow.Dtos;

namespace DomainService.Workflow.Services
{
    public interface IWorkflowVersionService
    {
        Task<BaseMutationResponse> CreateVersionAsync(string tenantId, WorkflowVersionCreateRequestDto dto);

        Task<BaseMutationResponse> UpdateVersionAsync(string tenantId, WorkflowVersionUpdateRequestDto dto);

        Task<WorkflowGetVersionsResponseDto> GetWorkflowVersionsAsync(string tenantId, WorkflowGetVersionsRequestDto dto);
    }
}