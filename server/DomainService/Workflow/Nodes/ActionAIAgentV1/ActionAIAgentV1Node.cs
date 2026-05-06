using System.Text.Json;
using System.Net.WebSockets;
using System.Text;
using MongoDB.Bson;
using DomainService.Workflow.Utils;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Nodes.ActionAIAgentV1
{
    [ExcludeFromCodeCoverage]
    public class ActionAIAgentV1Node : NodeExecutorBase<ActionAIAgentV1Parameters>
    {
        public override string NodeType => "agent";
        public override string Version => "1.0";
        private readonly HttpClient _httpClient;

        public ActionAIAgentV1Node()
        {
            _httpClient = new HttpClient();
        }

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, ActionAIAgentV1Parameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new ActionAIAgentV1Parameters();
                var outputItems = new List<NodeOutputItem>();

                for (int i = 0; i < context.IterationCount; i++)
                {
                    var input = parseExpression<string>(parameters.Input, context.InputItems[i], context) ?? "";
                    var response = await CallAIAgent(parameters.ApiBaseUrl, parameters.WidgetId, parameters.ProjectKey, input);

                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData
                        {
                            Input = context.InputItems[i].Data.Output,
                            Output = BsonJsonConverter.ToBsonValue(response),
                            Parameters = parameters.ToBsonDocument(),
                        },
                        Branch = "source",
                        ParentItemIds = new List<string>() { context.InputItems[i].Id },
                    });
                }

                return NodeExecutionResult.Successful(outputItems);
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<ActionAIAgentV1Parameters>(parameters);
                var isValid = config != null &&
                              !string.IsNullOrEmpty(config.WidgetId) &&
                              !string.IsNullOrEmpty(config.ProjectKey);
                return Task.FromResult(isValid);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }



        private async Task<JsonElement> CallAIAgent(string apiBaseUrl, string widgetId, string projectKey, string message)
        {
            try
            {
                // 1. Initiate
                var initiateUrl = $"{apiBaseUrl}/conversation/initiate?widget_id={widgetId}";
                var request = new HttpRequestMessage(HttpMethod.Get, initiateUrl);
                request.Headers.Add("X-Blocks-Key", projectKey);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var initiateJson = await response.Content.ReadAsStringAsync();
                var initiateData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(initiateJson)
                    ?? throw new Exception("Invalid initiate response");

                var websocketPath = initiateData["websocket_url"].GetString();
                var token = initiateData["token"].GetString();

                if (string.IsNullOrEmpty(websocketPath) || string.IsNullOrEmpty(token))
                    throw new Exception("Missing websocket_url or token");

                // 2. WebSocket connect
                var wsUrl =
                    $"{apiBaseUrl}{websocketPath}" +
                    $"?token={token}&x_blocks_key={projectKey}&pg=true&send_event=true";

                wsUrl = wsUrl.Replace("https://", "wss://").Replace("http://", "ws://");

                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

                // 3. Send user message
                var payload = JsonSerializer.Serialize(new { message });
                var bytes = Encoding.UTF8.GetBytes(payload);

                await ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );

                // 4. Receive events
                var buffer = new byte[16 * 1024];

                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    Dictionary<string, JsonElement>? evt;
                    try
                    {
                        evt = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    }
                    catch
                    {
                        continue;
                    }

                    if (evt == null || !evt.TryGetValue("type", out var typeEl))
                        continue;

                    var type = typeEl.GetString();

                    // 5. Final response event - Return only the message content
                    if (type == "chat_response" && evt.TryGetValue("message", out var msgEl))
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

                        // Parse the message content - it can be any structure
                        return msgEl;
                    }

                    // other events: typing, tool_call, status, etc → ignore
                }

                throw new Exception("chat_response event not received");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ActionAIAgentV1] Error in CallAIAgent: {ex.Message}");
                return new JsonElement(); // Return empty JSON on error

            }
        }

    }
}
