using DomainService.Workflow.Entities;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Events;
using DomainService.Workflow.Nodes.TriggerDataV1;
using DomainService.Workflow.Nodes.TriggerScheduleV1;
using DomainService.Workflow.Enums;
using System.Text.Json;



namespace DomainService.Workflow.Services
{

    public interface IWorkflowExecutionService
    {
        Task<WorkflowWebhookResponseDto> TriggerWebhookAsync(string workflowId, string triggerId, string tenantId, JsonElement input);
        Task<WorkflowWebhookResponseDto> TriggerTestWebhookAsync(string workflowId, string triggerId, string tenantId, JsonElement input);
        Task<WorkflowExecutionEntity> CreateExecutionAsync(WorkflowEntity workflow, TriggerMetadata triggerMetadata, WorkflowExecutionMode executionMode);
        Task<WorkflowExecutionsGetResponseDto> GetExecutionsByWorkflowIdAsync(string projectKey, WorkflowExecutionsGetRequestDto dto);
        Task<WorkflowExecutionGetResponseDto> GetExecutionByIdAsync(string projectKey, WorkflowExecutionGetRequestDto dto);
        Task<WorkflowExecutionGetResponseDto> LastSuccessfullExecutionAsync(string projectKey, LastSuccessfullExecutionRequestDto dto);
        Task EmailTriggerStartAsync(EmailTriggerEvent emailEvent);
        Task DataTriggerStartAsync(DataChangeEvent dataEvent);
        Task SchedulerTriggerStartAsync(SchedulerTriggerPayload payload);
        Task<StepExecuteResponseDto> StepExecuteAsync(string tenantId, StepExecuteRequestDto dto);
    }
}
