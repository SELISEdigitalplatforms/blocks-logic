using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Services;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Utils;

namespace Workflow.DomainService.Nodes.ActionProxy
{
    /// <summary>
    /// Calls a proxy configured in the Proxy module. The forward runs in-process through
    /// <see cref="IProxyGatewayService"/> rather than over the public
    /// <c>/api/proxy/gateway/{slug}/{**path}</c> route, so there is no HTTP hop and no inbound
    /// credential to assemble: the run's tenant is trusted directly. The proxy still owns the
    /// upstream URL, header and query injection, secret resolution and response shaping, and the
    /// call is recorded in that proxy's execution log exactly like a data-plane call.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class ActionProxyNode : NodeExecutorBase<ActionProxyParameters>
    {
        public override string NodeType => "proxy";
        public override string Version => "v1";

        private static readonly HashSet<string> MethodsWithBody =
            new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH" };

        private readonly IProxyGatewayService _gatewayService;
        private readonly ILogger<ActionProxyNode> _logger;

        public ActionProxyNode(
            IProxyGatewayService gatewayService,
            ILogger<ActionProxyNode> logger)
        {
            _gatewayService = gatewayService;
            _logger = logger;
        }

        protected override async Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context, ActionProxyParameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new ActionProxyParameters();
                if (string.IsNullOrWhiteSpace(parameters.Slug))
                    return NodeExecutionResult.Failed("No proxy is selected on this node.");

                var method = string.IsNullOrWhiteSpace(parameters.HttpMethod)
                    ? "GET"
                    : parameters.HttpMethod.Trim().ToUpperInvariant();

                var outputItems = new List<NodeOutputItem>();

                for (int i = 0; i < context.IterationCount; i++)
                {
                    var inputItem = context.InputItems[i];

                    var (body, contentType, bodyError) = PrepareBody(parameters, method, inputItem, context);
                    if (bodyError != null)
                        return NodeExecutionResult.Failed(bodyError);

                    var pathSuffix = (parseExpression<string>(parameters.Path, inputItem, context) ?? string.Empty)
                        .Trim()
                        .TrimStart('/');

                    var result = await _gatewayService.ForwardAsync(new ProxyForwardRequest
                    {
                        TenantId = context.TenantId,
                        UserId = BlocksContext.GetContext()?.UserId,
                        Slug = parameters.Slug,
                        Method = method,
                        PathSuffix = pathSuffix,
                        RequestPath = BuildRequestPath(parameters.Slug, pathSuffix),
                        Body = body,
                        ContentType = contentType,
                        IsTest = false,
                    }, context.CancellationToken);

                    if (!result.Ok)
                        return NodeExecutionResult.Failed(DescribeFailure(parameters.Slug, method, result));

                    var (responseBody, parseError) = ParseResponse(result);
                    if (parseError != null)
                        return NodeExecutionResult.Failed(parseError);

                    BuildOutputItems(outputItems, responseBody, context, parameters, i);
                }

                return NodeExecutionResult.Successful(outputItems);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Resolves expressions in the configured body and validates it as JSON before any upstream
        /// connection. Returns a non-null error when the body is enabled but not parseable.
        /// </summary>
        private (byte[]? body, string? contentType, string? error) PrepareBody(
            ActionProxyParameters parameters,
            string method,
            WorkflowItemExecutionEntity inputItem,
            NodeExecutionContext context)
        {
            if (!parameters.HaveBody || !MethodsWithBody.Contains(method))
                return (null, null, null);

            var bodyContent = parseExpression<string>(parameters.Body.Trim(), inputItem, context);
            if (string.IsNullOrWhiteSpace(bodyContent))
                return (null, null, null);

            var contentType = GetContentType(parameters.BodyContentType);
            if (contentType == "application/json")
            {
                try
                {
                    JsonDocument.Parse(bodyContent);
                }
                catch (JsonException ex)
                {
                    _logger.LogError("Proxy node: body is not valid JSON for proxy {Slug}.", parameters.Slug);
                    return (null, null, $"Invalid JSON body: {ex.Message}");
                }
            }

            return (Encoding.UTF8.GetBytes(bodyContent), contentType, null);
        }

        /// <summary>
        /// The client-facing path recorded on the proxy execution row. Kept identical in shape to the
        /// data-plane route so proxy logs read the same whether the caller was a workflow or a client.
        /// </summary>
        private static string BuildRequestPath(string slug, string pathSuffix) =>
            string.IsNullOrEmpty(pathSuffix)
                ? $"/api/proxy/gateway/{slug}"
                : $"/api/proxy/gateway/{slug}/{pathSuffix}";

        /// <summary>
        /// Turns a non-success forward into a message that says what the proxy decided, rather than
        /// surfacing a bare status code. A 405 also lists the methods the proxy actually allows,
        /// which is the failure most likely to follow an edit to the proxy after the node was saved.
        /// </summary>
        private static string DescribeFailure(string slug, string method, ProxyForwardResult result)
        {
            var detail = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : $" {result.ErrorMessage}";

            if (result.Outcome == ProxyExecutionOutcome.MethodNotAllowed)
            {
                var allowed = result.AllowedMethods.Count > 0
                    ? string.Join(", ", result.AllowedMethods)
                    : "none";
                return $"Proxy '{slug}' does not allow {method}. Allowed: {allowed}.";
            }

            var upstream = result.UpstreamStatusCode.HasValue
                ? $" Upstream returned {result.UpstreamStatusCode.Value}."
                : "";

            return $"Proxy '{slug}' call failed with {result.Outcome} ({result.StatusCode}).{upstream}{detail}";
        }

        /// <summary>
        /// Reads the relayed body as JSON. An empty body becomes an empty object so a no-content
        /// response still produces one output item instead of failing the node.
        /// </summary>
        private static (JsonElement body, string? error) ParseResponse(ProxyForwardResult result)
        {
            var raw = result.ResponseBody;
            if (string.IsNullOrWhiteSpace(raw))
                return (JsonDocument.Parse("{}").RootElement.Clone(), null);

            try
            {
                return (JsonDocument.Parse(raw).RootElement.Clone(), null);
            }
            catch (JsonException)
            {
                var contentType = string.IsNullOrWhiteSpace(result.ResponseContentType)
                    ? "an unknown content type"
                    : result.ResponseContentType;
                return (default, $"Proxy response is not valid JSON (Content-Type: {contentType}).");
            }
        }

        private static void BuildOutputItems(
            List<NodeOutputItem> outputItems, JsonElement responseBody,
            NodeExecutionContext context, ActionProxyParameters parameters, int index)
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
                    ParentItemIds = new List<string> { context.InputItems[index].Id }
                });
            }
        }

        private static string GetContentType(string bodyContentType) =>
            bodyContentType.ToLower() switch
            {
                "json" => "application/json",
                "xml" => "application/xml",
                "text" => "text/plain",
                "html" => "text/html",
                _ => string.IsNullOrWhiteSpace(bodyContentType) ? "application/json" : bodyContentType
            };
    }
}
