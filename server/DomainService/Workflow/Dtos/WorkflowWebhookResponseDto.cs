using System.Text.Json;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowWebhookResponseDto
    {
        public string ExecutionId { get; set; }
        public string Status { get; set; }
        public JsonElement? Data { get; set; }
    }
}