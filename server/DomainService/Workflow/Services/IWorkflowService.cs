using Blocks.Genesis;
using DomainService.Workflow.Dtos;

namespace DomainService.Workflow.Services
{
    public interface IWorkflowService
    {
        Task<BaseMutationResponse> CreateAsync(string tenantId, WorkflowCreateRequestDto dto);

        Task<BaseMutationResponse> DuplicateAsync(string tenantId, WorkflowDuplicateRequestDto dto);

        Task<WorkflowGetsResponseDto> GetAllAsync(string tenantId, WorkflowGetsRequestDto dto);

        Task<WorkflowGetResponseDto> GetAsync(string tenantId, WorkflowGetRequestDto dto);

        Task<BaseMutationResponse> UpdateAsync(string tenantId, WorkflowUpdateRequestDto dto);

        Task<BaseMutationResponse> DeleteAsync(string tenantId, WorkflowDeleteRequestDto dto);

        Task<BaseMutationResponse> CreateVersionAsync(string tenantId, WorkflowVersionCreateRequestDto dto);

        Task<BaseMutationResponse> UpdateVersionAsync(string tenantId, WorkflowVersionUpdateRequestDto dto);

        Task<WorkflowGetVersionsResponseDto> GetVersionsAsync(string tenantId, WorkflowGetVersionsRequestDto dto);

        Task<GetWorkflowByVersionResponseDto> GetWorkflowByVersionAsync(string tenantId, GetWorkflowByVersionRequestDto dto);

        Task<BaseMutationResponse> PublishNewVersionAsync(string tenantId, WorkflowPublishNewVersionRequestDto dto);

        Task<BaseMutationResponse> PublishVersionAsync(string tenantId, WorkflowPublishVersionRequestDto dto);

        Task<BaseMutationResponse> UnpublishAsync(string tenantId, WorkflowUnpublishRequestDto dto);

        Task<BaseMutationResponse> RestoreAsync(string tenantId, WorkflowRestoreRequestDto dto);

        Task<BaseMutationResponse> TriggerListenerAsync(string tenantId, TriggerListenerRequestDto dto);
    }
}