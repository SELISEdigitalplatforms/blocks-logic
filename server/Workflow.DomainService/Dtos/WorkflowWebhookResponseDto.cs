using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowWebhookResponseDto
    {
        /// <summary>Status the webhook controller should answer with when the execution failed.</summary>
        public const string FailedStatus = "Failed";

        public string ExecutionId { get; set; }
        public string Status { get; set; }
        public JsonElement? Data { get; set; }

        /// <summary>
        /// Present only when <see cref="Status"/> is <see cref="FailedStatus"/>: which node stopped the
        /// workflow and its caller-safe message. Omitted from the JSON otherwise.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorkflowWebhookErrorDto? Error { get; set; }
    }

    public class WorkflowWebhookErrorDto
    {
        public string? NodeId { get; set; }
        public string? NodeName { get; set; }
        public required string Message { get; set; }
    }
}
