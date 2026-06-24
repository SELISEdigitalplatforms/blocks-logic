using DomainService.Workflow.Models;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Events;
using DomainService.Workflow.Nodes.TriggerDataV1;
using DomainService.Workflow.Enums;
using System.Text.Json;



namespace DomainService.Workflow.Services
{

    public interface IWorkflowExecutionService
    {
        Task<WorkflowWebhookResponseDto> TriggerWebhookAsync(string workflowId, string triggerId, string tenantId, JsonElement input);
        Task<WorkflowWebhookResponseDto> TriggerTestWebhookAsync(string workflowId, string triggerId, string tenantId, JsonElement input);
        Task<WorkflowExecutionModel> CreateExecutionAsync(WorkflowModel workflow, WorkflowExecutionMode executionMode);
        Task<WorkflowExecutionsGetResponseDto> GetExecutionsByWorkflowIdAsync(WorkflowExecutionsGetRequestDto dto);
        Task<WorkflowExecutionGetResponseDto> GetExecutionByIdAsync(WorkflowExecutionGetRequestDto dto);
        Task EmailTriggerStartAsync(EmailTriggerEvent emailEvent);
        Task DataTriggerStartAsync(DataChangeEvent dataEvent);
    }
}
