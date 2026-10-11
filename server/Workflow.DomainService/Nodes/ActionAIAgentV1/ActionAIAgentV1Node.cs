using System.Text.Json;
using System.Net.WebSockets;
using System.Text;
using MongoDB.Bson;
using Workflow.DomainService.Utils;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Workflow.DomainService.Logging;

namespace Workflow.DomainService.Nodes.ActionAIAgentV1
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
            var parameters = nodeparameters ?? new ActionAIAgentV1Parameters();
            var outputItems = new List<NodeOutputItem>();

            for (int i = 0; i < context.IterationCount; i++)
            {
                // Per-item lines are capped like the other network-bound nodes; never the prompt or reply.
                var logThisItem = i < ExecutionLogStages.PerItemLineCap;
                if (i == ExecutionLogStages.PerItemLineCap)
                {
                    context.Log.Info(ExecutionLogStages.AgentNotLogged, "… {Remaining} more request(s) not logged individually.",
                        context.IterationCount - ExecutionLogStages.PerItemLineCap);
                }

                try
                {
                    var input = parseExpression<string>(parameters.Input, context.InputItems[i], context) ?? "";
                    if (logThisItem)
                    {
                        context.Log.Info(ExecutionLogStages.AgentCall, "Calling AI agent for item {Index} of {Total}.", i + 1, context.IterationCount);
                    }
                    var stopwatch = Stopwatch.StartNew();
                    var response = await CallAIAgent(parameters.ApiBaseUrl, parameters.WidgetId, context.TenantId, input);
                    if (logThisItem)
                    {
                        context.Log.Info(ExecutionLogStages.AgentResponse, "Agent responded in {DurationMs} ms.", stopwatch.ElapsedMilliseconds);
                    }

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
                catch (Exception ex)
                {
                    AppendErrorOutputItem(outputItems, context.InputItems[i], parameters.ToBsonDocument(), ex);
                }
            }

            return NodeExecutionResult.Successful(outputItems);
        }

        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<ActionAIAgentV1Parameters>(parameters);
                var isValid = config != null &&
                              !string.IsNullOrEmpty(config.WidgetId);
                return Task.FromResult(isValid);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }



        /// <summary>
        /// One agent call. Any failure (HTTP error, bad initiate response, no chat_response in the stream)
        /// throws with a clear message; the per-item catch turns it into an error item, like the HTTP
        /// Request node. It used to be swallowed (Console.WriteLine + empty JsonElement), so the item only
        /// said "Operation is not valid due to the current state of the object." and the cause was lost.
        /// </summary>
        private async Task<JsonElement> CallAIAgent(string apiBaseUrl, string widgetId, string tenantId, string message)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
                throw new InvalidOperationException("AI agent request failed: no API base URL is set on this node.");
            if (string.IsNullOrWhiteSpace(widgetId))
                throw new InvalidOperationException("AI agent request failed: no agent (widget) is selected on this node.");

            // 1. Initiate Chat Session
            var initiateUrl = $"{apiBaseUrl}/conversation/initiate?widget_id={widgetId}";

            var initiateRequest = new HttpRequestMessage(HttpMethod.Get, initiateUrl);
            initiateRequest.Headers.Add("X-Blocks-Key", tenantId);

            using var initiateResponse = await SendAsync(initiateRequest, HttpCompletionOption.ResponseContentRead);
            EnsureSuccess(initiateResponse, "starting the conversation");

            var initiateJson = await initiateResponse.Content.ReadAsStringAsync();
            AgentChatInitiateResponse? initiateData;
            try
            {
                initiateData = JsonSerializer.Deserialize<AgentChatInitiateResponse>(initiateJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("AI agent request failed: the conversation start response is not valid JSON.");
            }
            if (initiateData == null || string.IsNullOrWhiteSpace(initiateData.SessionId))
                throw new InvalidOperationException("AI agent request failed: the conversation start response has no session id.");

            var chatRequestUrl = $"{apiBaseUrl}/chat/{initiateData.SessionId}?pg=false";
            var chatRequest = new HttpRequestMessage(HttpMethod.Post, chatRequestUrl);
            chatRequest.Headers.Add("X-Blocks-Key", tenantId);
            if (!string.IsNullOrEmpty(initiateData.Token))
                chatRequest.Headers.Add("X-Blocks-Token", initiateData.Token);
            chatRequest.Content = new StringContent(JsonSerializer.Serialize(new { message }), Encoding.UTF8, "application/json");
            using var chatResponse = await SendAsync(chatRequest, HttpCompletionOption.ResponseHeadersRead);
            EnsureSuccess(chatResponse, "sending the message");
            await using var stream = await chatResponse.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
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
                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(json);
                    }
                    catch (JsonException)
                    {
                        throw new InvalidOperationException("AI agent request failed: the agent's reply is not valid JSON.");
                    }

                    using (doc)
                    {
                        if (doc.RootElement.ValueKind == JsonValueKind.Object
                            && doc.RootElement.TryGetProperty("message", out var msg))
                        {
                            return msg.Clone();
                        }
                    }
                }
            }

            throw new InvalidOperationException("AI agent request failed: the agent closed the stream without a reply.");
        }

        private static void EnsureSuccess(HttpResponseMessage response, string step)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"AI agent request failed while {step}: HTTP {(int)response.StatusCode}.");
        }

        /// <summary>Seam for tests; production sends with the node's own HttpClient.</summary>
        protected virtual Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion)
            => _httpClient.SendAsync(request, completion);

    }
}
