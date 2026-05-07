using System.Text.Json.Serialization;

namespace DomainService.Workflow.Nodes.ActionAIAgentV1
{
    public class ActionAIAgentV1Parameters
    {
        public string agent { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string WidgetId { get; set; } = string.Empty;
        public string Input { get; set; } = string.Empty;
        public string ProjectKey { get; set; } = string.Empty;
        public string ApiBaseUrl { get; set; } = string.Empty;
    }

    public class AgentChatInitiateResponse
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = default!;

        [JsonPropertyName("token")]
        public string Token { get; set; } = default!;

        [JsonPropertyName("websocket_url")]
        public string WebsocketUrl { get; set; } = default!;

        [JsonPropertyName("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("is_success")]
        public bool IsSuccess { get; set; }

        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }
}
