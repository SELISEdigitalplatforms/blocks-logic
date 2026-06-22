using Blocks.Genesis;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Services
{
    public interface IWorkflowService
    {
        Task<BaseMutationResponse> CreateAsync(WorkflowCreateRequestDto dto);

        Task<BaseMutationResponse> DuplicateAsync(WorkflowDuplicateRequestDto dto);
        Task<WorkflowGetsResponseDto> GetAllAsync(WorkflowGetsRequestDto dto);

        Task<WorkflowGetResponseDto> GetAsync(WorkflowGetRequestDto dto);
        Task<BaseMutationResponse> UpdateAsync(WorkflowUpdateRequestDto dto);
        Task<BaseMutationResponse> DeleteAsync(WorkflowDeleteRequestDto dto);

        Task<BaseMutationResponse> CreateVersion(WorkflowVersionCreateRequestDto dto);

        Task<WorkflowGetVersionsResponseDto> GetVersions(WorkflowGetVersionsRequestDto dto);

        Task<BaseMutationResponse> PublishAsync(WorkflowPublishRequestDto dto);

        Task<BaseMutationResponse> RestoreAsync(WorkflowRestoreRequestDto dto);
    }
}