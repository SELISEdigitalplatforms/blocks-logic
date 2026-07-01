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

        Task<BaseMutationResponse> CreateVersionAsync(WorkflowVersionCreateRequestDto dto);
        Task<BaseMutationResponse> UpdateVersionAsync(WorkflowVersionUpdateRequestDto dto);
        Task<WorkflowGetVersionsResponseDto> GetVersionsAsync(WorkflowGetVersionsRequestDto dto);

        Task<GetWorkflowByVersionResponseDto> GetWorkflowByVersionAsync(GetWorkflowByVersionRequestDto dto);

        Task<BaseMutationResponse> PublishNewVersionAsync(WorkflowPublishNewVersionRequestDto dto);

        Task<BaseMutationResponse> PublishVersionAsync(WorkflowPublishVersionRequestDto dto);

        Task<BaseMutationResponse> UnpublishAsync(WorkflowUnpublishRequestDto dto);

        Task<BaseMutationResponse> RestoreAsync(WorkflowRestoreRequestDto dto);
    }
}