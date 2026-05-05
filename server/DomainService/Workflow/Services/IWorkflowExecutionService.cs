using DomainService.Workflow.Models;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Events;
using DomainService.Workflow.Nodes.TriggerDataV1;
using MongoDB.Bson;
using System.Text.Json;



namespace DomainService.Workflow.Services
{

    public interface IWorkflowExecutionService
    {
        Task<WorkflowExecutionModel> CreateExecutionAsync(string workflowId, string triggerId, string tenantId);
        Task<WorkflowWebhookResponseDto> WebhookStartAsync(string workflowId, string triggerId, string tenantId, JsonElement input);
        Task<WorkflowExecutionsGetResponseDto> GetExecutionsByWorkflowIdAsync(WorkflowExecutionsGetRequestDto dto);
        Task<WorkflowExecutionGetResponseDto> GetExecutionByIdAsync(WorkflowExecutionGetRequestDto dto);

        Task EmailTriggerStartAsync(EmailTriggerEvent emailEvent);

        Task DataTriggerStartAsync(DataChangeEvent dataEvent);

    }
}
