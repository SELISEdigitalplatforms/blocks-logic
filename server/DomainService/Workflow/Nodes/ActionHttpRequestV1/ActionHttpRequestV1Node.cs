
using System.Text.Json;
using DomainService.MagicLink.Models;
using DomainService.MagicLink.Service;
using DomainService.Workflow.Services;
using DomainService.Workflow.Utils;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;
using DomainService.Workflow.Entities;
using Blocks.Genesis;

namespace DomainService.Workflow.Nodes.ActionHttpRequestV1
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
            try
            {
                var parameters = nodeparameters ?? new ActionHttpRequestV1Parameters();
                var outputItems = new List<NodeOutputItem>();

                for (int i = 0; i < context.IterationCount; i++)
                {
                    var (url, httpMethod, headers, bodyContent, contentType) = PrepareRequest(parameters, context.InputItems[i], context);
                    if (url == null)
                        return NodeExecutionResult.Failed(bodyContent);

                    await ApplyAuthenticationAsync(parameters, headers, context.TenantId);

                    var responseBody = await SendHttpRequestAsync(httpMethod, url, headers, bodyContent, contentType);
                    BuildOutputItems(outputItems, responseBody, context, parameters, i);
                }
                return NodeExecutionResult.Successful(outputItems);
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Adds a bearer token when Authentication is Blocks Authentication or Client Credential
        /// and no Authorization header was already set manually (manual value always wins).
        /// Best effort: silently leaves headers untouched when no token is available.
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
                token = await _workflowAuthService.CreateBlocksAuthorizationTokenAsync();
                if (string.IsNullOrWhiteSpace(token))
                    token = BlocksContext.GetContext()?.OAuthToken;
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

        private (string? url, string httpMethod, Dictionary<string, string> headers, string bodyContent, string contentType) PrepareRequest(
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
                bodyContent = parseExpression<string>(parameters.Body.Trim(), inputItem, context);

                if (parameters.BodyContentType.ToLower() == "json")
                {
                    try { JsonDocument.Parse(bodyContent); }
                    catch (JsonException ex)
                    {
                        _logger.LogError(bodyContent);
                        return (null, httpMethod, headers, $"Invalid JSON body: {ex.Message}", "");
                    }
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
        private async Task<JsonElement> SendHttpRequestAsync(string httpMethod, string url, Dictionary<string, string> headers, string bodyContent, string contentType = "application/json")
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
