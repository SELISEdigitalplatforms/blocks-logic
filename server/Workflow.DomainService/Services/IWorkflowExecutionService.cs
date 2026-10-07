using Workflow.DomainService.Entities;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Nodes.TriggerDataV1;
using Workflow.DomainService.Nodes.TriggerScheduleV1;
using Workflow.DomainService.Enums;
using Mail.DomainService.Mails;
using System.Text.Json;



namespace Workflow.DomainService.Services
{

    public interface IWorkflowExecutionService
    {
        Task<WorkflowWebhookResponseDto> TriggerWebhookAsync(string workflowId, string triggerId, string tenantId, JsonElement input);
        Task<WorkflowWebhookResponseDto> TriggerTestWebhookAsync(string workflowId, string triggerId, string tenantId, JsonElement input);
        Task<WorkflowExecutionEntity> CreateExecutionAsync(WorkflowEntity workflow, TriggerMetadata triggerMetadata, WorkflowExecutionMode executionMode);
        Task<WorkflowExecutionsGetResponseDto> GetExecutionsByWorkflowIdAsync(string tenantId, WorkflowExecutionsGetRequestDto dto);
        Task<WorkflowExecutionGetResponseDto> GetExecutionByIdAsync(string tenantId, WorkflowExecutionGetRequestDto dto);

        /// <summary>
        /// Continues a failed production execution from where it stopped: completed steps are not run again,
        /// and the failed step reuses what it had already done (the function step, per item).
        /// </summary>
        Task<WorkflowExecutionResumeResponseDto> ResumeExecutionAsync(string tenantId, WorkflowExecutionGetRequestDto dto);
        Task<WorkflowExecutionGetResponseDto> LastSuccessfullExecutionAsync(string tenantId, LastSuccessfullExecutionRequestDto dto);
        Task EmailTriggerStartAsync(EmailTriggerEvent emailEvent);
        Task DataTriggerStartAsync(DataChangeEvent dataEvent);
        Task SchedulerTriggerStartAsync(SchedulerTriggerPayload payload);
        Task<StepExecuteResponseDto> StepExecuteAsync(string tenantId, StepExecuteRequestDto dto);
    }
}
