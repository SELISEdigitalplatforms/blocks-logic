
using System.Text.Json;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;
using Workflow.DomainService.Entities;
using Blocks.Genesis;
using System.Diagnostics;
using Workflow.DomainService.Logging;

namespace Workflow.DomainService.Nodes.ActionHttpRequestV1
{
    [ExcludeFromCodeCoverage]
    public class ActionHttpRequestV1Node : NodeExecutorBase<ActionHttpRequestV1Parameters>
    {
        public override string NodeType => "httpRequest";
        public override string Version => "v1";

        private const string AuthorizationHeaderName = "Authorization";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWorkflowAuthService _workflowAuthService;
        private readonly IClientCredentialTokenService _clientCredentialTokenService;
        private readonly ILogger<ActionHttpRequestV1Node> _logger;

        public ActionHttpRequestV1Node(
            IHttpClientFactory httpClientFactory,
            IWorkflowAuthService workflowAuthService,
            IClientCredentialTokenService clientCredentialTokenService,
            ILogger<ActionHttpRequestV1Node> logger)
        {
            _httpClientFactory = httpClientFactory;
            _workflowAuthService = workflowAuthService;
            _clientCredentialTokenService = clientCredentialTokenService;
            _logger = logger;
        }

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, ActionHttpRequestV1Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new ActionHttpRequestV1Parameters();
            parameters.HaveBody = ReadHaveBody(context.Parameters, parameters.HaveBody);
            var outputItems = new List<NodeOutputItem>();
            var requestLog = PerItemRequestLog.ForHttp(context.Log, context.IterationCount);

            for (int i = 0; i < context.IterationCount; i++)
            {
                try
                {
                    string url, httpMethod, bodyContent, contentType;
                    Dictionary<string, string> headers;
                    try
                    {
                        (url, httpMethod, headers, bodyContent, contentType) = PrepareRequest(parameters, context.InputItems[i], context);
                    }
                    catch (InvalidFilledJsonException ex)
                    {
                        // Nothing was sent for this item. The configured body is broken, so the step
                        // fails; items before it already ran and are kept.
                        requestLog.Failed(i, "InvalidJsonBody");
                        return NodeExecutionResult.Failed(ex.ForItem(i), outputItems);
                    }

                    await ApplyAuthenticationAsync(parameters, headers, context.TenantId);

                    requestLog.Sending(i);
                    var stopwatch = Stopwatch.StartNew();
                    var index = i;
                    var responseBody = await SendHttpRequestAsync(httpMethod, url, headers, bodyContent, contentType,
                        onStatus: status => requestLog.Response(index, status, stopwatch.ElapsedMilliseconds));
                    BuildOutputItems(outputItems, responseBody, context, parameters, i);
                }
                catch (Exception ex)
                {
                    // The type only: the message can carry the URL or the response.
                    requestLog.Failed(i, (ex.InnerException ?? ex).GetType().Name);
                    AppendErrorOutputItem(outputItems, context.InputItems[i], parameters.ToBsonDocument(), ex);
                }
            }
            return NodeExecutionResult.Successful(outputItems);
        }

        /// <summary>
        /// Adds a bearer token when Authentication is Blocks Authentication or Client Credential
        /// and no Authorization header was already set manually (manual value always wins).
        /// Blocks Authentication without a delegated token throws <see cref="NoDelegatedTokenException"/>
        /// (the item fails). Client Credential keeps its behaviour.
        /// </summary>
        private async Task ApplyAuthenticationAsync(
            ActionHttpRequestV1Parameters parameters,
            Dictionary<string, string> headers,
            string tenantId)
        {
            if (headers.Keys.Any(key => string.Equals(key, AuthorizationHeaderName, StringComparison.OrdinalIgnoreCase)))
                return;

            var mode = parameters.AuthenticationType;
            if (string.IsNullOrWhiteSpace(mode) && parameters.UseBlocksAuthorization)
                mode = "blocksAuthentication";

            string? token = null;
            if (string.Equals(mode, "blocksAuthentication", StringComparison.OrdinalIgnoreCase))
            {
                // Delegated token only. Never the caller's raw BlocksContext.OAuthToken, and never an
                // unauthenticated send: no token fails this item (WS-1).
                token = await _workflowAuthService.CreateBlocksAuthorizationTokenAsync();
                if (string.IsNullOrWhiteSpace(token))
                    throw new NoDelegatedTokenException();
            }
            else if (string.Equals(mode, "clientCredential", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(parameters.ClientId)
                && !string.IsNullOrWhiteSpace(parameters.ClientSecret))
            {
                token = await _clientCredentialTokenService.GetTokenAsync(
                    new ClientCredential { ItemId = parameters.ClientId, ClientSecret = parameters.ClientSecret },
                    tenantId);
            }

            if (string.IsNullOrWhiteSpace(token)) return;
            headers[AuthorizationHeaderName] = $"Bearer {token}";
        }

        private (string url, string httpMethod, Dictionary<string, string> headers, string bodyContent, string contentType) PrepareRequest(
            ActionHttpRequestV1Parameters parameters, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            var url = parseExpression<string>(parameters.Url, inputItem, context) ?? "";
            var httpMethod = parameters.HttpMethod.ToUpper();

            var queryParams = new Dictionary<string, string>();
            if (parameters.HaveQueryParameters)
            {
                queryParams = parameters.QueryParameters.Keys.ToDictionary(
                    key => parseExpression<string>(key, inputItem, context) ?? "",
                    key => parseExpression<string>(parameters.QueryParameters[key], inputItem, context) ?? ""
                );
            }

            var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            url += url.Contains("?") ? $"&{queryString}" : $"?{queryString}";

            var headers = new Dictionary<string, string>();
            if (parameters.HaveHeaders)
            {
                headers = parameters.Headers.Keys.ToDictionary(
                    key => parseExpression<string>(key, inputItem, context) ?? "",
                    key => parseExpression<string>(parameters.Headers[key], inputItem, context) ?? ""
                );
            }

            var bodyContent = string.Empty;
            if (parameters.HaveBody)
            {
                var body = (parameters.Body ?? string.Empty).Trim();
                if (string.Equals(parameters.BodyContentType, "json", StringComparison.OrdinalIgnoreCase))
                {
                    // Values are filled JSON-safe (escaped inside "…", typed outside), then the body must be
                    // valid JSON or the step fails. A blank body sends no body, as for other content types.
                    // The body is never logged: it can carry secrets.
                    bodyContent = body.Length == 0
                        ? string.Empty
                        : FillJsonTemplateOrThrow(body, "Body", inputItem, context);
                }
                else
                {
                    bodyContent = parseExpression<string>(body, inputItem, context) ?? string.Empty;
                }
            }

            var contentType = parameters.HaveBody ? GetContentType(parameters.BodyContentType) : "application/json";
            return (url, httpMethod, headers, bodyContent, contentType);
        }

        private static void BuildOutputItems(
            List<NodeOutputItem> outputItems, JsonElement responseBody,
            NodeExecutionContext context, ActionHttpRequestV1Parameters parameters, int index)
        {
            var bodyItems = responseBody.ValueKind == JsonValueKind.Array
                ? responseBody.EnumerateArray().ToList()
                : new List<JsonElement> { responseBody };

            foreach (var bodyItem in bodyItems)
            {
                outputItems.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = context.InputItems[index].Data.Output,
                        Output = BsonJsonConverter.ToBsonValue(bodyItem),
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = "source",
                    ParentItemIds = new List<string>() { context.InputItems[index].Id }
                });
            }
        }
        // Send HTTP request and get response as list of items (to support array responses) like if response is an array, each element will be a separate item; if response is an object, it will be a single item
        private async Task<JsonElement> SendHttpRequestAsync(string httpMethod, string url, Dictionary<string, string> headers, string bodyContent, string contentType = "application/json", Action<int>? onStatus = null)
        {
            var httpClient = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(new HttpMethod(httpMethod), url);
            // Add headers
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }
            // Add body for all methods
            if (!string.IsNullOrEmpty(bodyContent))
            {
                request.Content = new StringContent(bodyContent, System.Text.Encoding.UTF8, contentType);
            }

            try
            {
                HttpResponseMessage response;
                response = await httpClient.SendAsync(request);
                onStatus?.Invoke((int)response.StatusCode);
                response.EnsureSuccessStatusCode();
                var responseString = await response.Content.ReadAsStringAsync();
                return JsonDocument.Parse(responseString).RootElement.Clone();
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"HTTP request failed: {ex.Message}", ex);
            }
        }


        private static string GetContentType(string bodyContentType)
        {
            return bodyContentType.ToLower() switch
            {
                "json" => "application/json",
                "xml" => "application/xml",
                "text" => "text/plain",
                "html" => "text/html",
                _ => bodyContentType // Use as-is if not recognized
            };
        }

        private static bool ReadHaveBody(BsonDocument rawParameters, bool fallback)
        {
            if (!rawParameters.TryGetValue("havebody", out var value))
                return fallback;

            if (value.IsBoolean)
                return value.AsBoolean;

            return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
        }




        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<ActionHttpRequestV1Parameters>(parameters);
                var isValid = config != null &&
                              !string.IsNullOrEmpty(config.HttpMethod) &&
                              !string.IsNullOrEmpty(config.Url);
                return Task.FromResult(isValid);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}
