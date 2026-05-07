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
                // 1. Initiate Chat Session
                var initiateUrl = $"{apiBaseUrl}/conversation/initiate?widget_id={widgetId}";

                var initiateRequest = new HttpRequestMessage(HttpMethod.Get, initiateUrl);
                initiateRequest.Headers.Add("X-Blocks-Key", projectKey);

                var initiateResponse = await _httpClient.SendAsync(initiateRequest);
                initiateResponse.EnsureSuccessStatusCode();

                var initiateJson = await initiateResponse.Content.ReadAsStringAsync();
                var initiateData = JsonSerializer.Deserialize<AgentChatInitiateResponse>(initiateJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new Exception("Invalid initiate response");

                var chatRequestUrl = $"{apiBaseUrl}/chat/{initiateData.SessionId}?project_key={projectKey}&pg=false";
                var chatRequest = new HttpRequestMessage(HttpMethod.Post, chatRequestUrl);
                chatRequest.Headers.Add("X-Blocks-Key", projectKey);
                chatRequest.Headers.Add("X-Blocks-Token", initiateData.Token);
                chatRequest.Content = new StringContent(JsonSerializer.Serialize(new { message }), Encoding.UTF8, "application/json");
                var chatResponse = await _httpClient.SendAsync(chatRequest, HttpCompletionOption.ResponseHeadersRead);
                chatResponse.EnsureSuccessStatusCode();
                await using var stream = await chatResponse.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                string? line;


                while (!reader.EndOfStream)
                {
                    line = await reader.ReadLineAsync();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (!line.StartsWith("event:"))
                        continue;

                    var eventType = line["event:".Length..].Trim();

                    // move to next line for data
                    var dataLine = await reader.ReadLineAsync();
                    if (dataLine == null || !dataLine.StartsWith("data:"))
                        continue;

                    var json = dataLine["data:".Length..].Trim();

                    if (eventType == "chat_response")
                    {
                        using var doc = JsonDocument.Parse(json);

                        if (doc.RootElement.TryGetProperty("message", out var msg))
                        {
                            return msg.Clone();
                        }
                    }
                }
                return JsonSerializer.Deserialize<JsonElement>("");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ActionAIAgentV1] Error in CallAIAgent: {ex.Message}");
                return new JsonElement(); // Return empty JSON on error

            }
        }

    }
}
