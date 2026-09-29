using System.Text.Json;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowWebhookResponseDto
    {
        public string ExecutionId { get; set; }
        public string Status { get; set; }
        public JsonElement? Data { get; set; }
    }
}