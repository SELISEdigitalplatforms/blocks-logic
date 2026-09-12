using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    /// Calls one of a proxy's declared routes. The forward runs in-process through
    /// <see cref="IProxyGatewayService"/> rather than over the public
    /// <c>/api/proxy/gateway/{slug}/{**path}</c> route, so there is no HTTP hop and no inbound
    /// credential to assemble: the run's tenant is trusted directly. The proxy still owns the upstream
    /// URL, header and query injection, secret resolution and response shaping, and the call is
    /// recorded in that proxy's execution log attributed to the workflow, run and node that issued it.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class ActionProxyNode : NodeExecutorBase<ActionProxyParameters>
    {
        public override string NodeType => "proxy";
        public override string Version => "v1";

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        /// <summary>A <c>{name}</c> segment of a client-facing route template.</summary>
        private static readonly Regex RouteParameter =
            new(@"\{([^}/]+)\}", RegexOptions.Compiled, RegexTimeout);

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
                if (string.IsNullOrWhiteSpace(parameters.RouteMethod))
                    return NodeExecutionResult.Failed("No endpoint is selected on this node.");

                var method = parameters.RouteMethod.Trim().ToUpperInvariant();
                var blocksContext = BlocksContext.GetContext();
                var outputItems = new List<NodeOutputItem>();

                for (int i = 0; i < context.IterationCount; i++)
                {
                    var inputItem = context.InputItems[i];

                    var (pathSuffix, pathError) = BuildPathSuffix(parameters, inputItem, context);
                    if (pathError != null)
                        return NodeExecutionResult.Failed(pathError);

                    var (body, contentType, bodyError) = PrepareBody(parameters, method, inputItem, context);
                    if (bodyError != null)
                        return NodeExecutionResult.Failed(bodyError);

                    var result = await _gatewayService.ForwardAsync(new ProxyForwardRequest
                    {
                        TenantId = context.TenantId,
                        UserId = blocksContext?.UserId,
                        UserName = blocksContext?.UserName,
                        // Marks the row as a workflow forward so the proxy log can tell it apart from a
                        // front-end call without inferring it from the path.
                        CallerKind = ProxyCallerKind.Workflow,
                        WorkflowId = context.WorkflowId,
                        WorkflowRunId = context.WorkflowExecutionId,
                        WorkflowNodeId = context.NodeId,
                        Slug = parameters.Slug,
                        Method = method,
                        PathSuffix = pathSuffix,
                        RequestPath = BuildRequestPath(parameters.Slug, pathSuffix),
                        Body = body,
                        ContentType = contentType,
                        IsTest = false,
                    }, context.CancellationToken);

                    if (!result.Ok)
                        return NodeExecutionResult.Failed(DescribeFailure(parameters, method, result));

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
        /// Substitutes the configured values into the route's <c>{name}</c> segments to produce the
        /// concrete path the gateway matches against the allowlist. Each value fills exactly one segment,
        /// so it is escaped as a single segment: an unescaped <c>/</c> would otherwise widen the path and
        /// land on an endpoint no route declared. A missing or empty value fails the node here rather
        /// than sending a path containing a literal <c>{name}</c> that could only be refused as unlisted.
        /// </summary>
        private (string path, string? error) BuildPathSuffix(
            ActionProxyParameters parameters,
            WorkflowItemExecutionEntity inputItem,
            NodeExecutionContext context)
        {
            var template = (parameters.RoutePath ?? string.Empty).Trim().Trim('/');
            if (template.Length == 0)
                return (string.Empty, null);

            string? missing = null;

            var resolved = RouteParameter.Replace(template, match =>
            {
                var name = match.Groups[1].Value.Trim();
                if (!parameters.PathParams.TryGetValue(name, out var raw))
                {
                    missing ??= name;
                    return match.Value;
                }

                var value = parseExpression<string>(raw, inputItem, context);
                if (string.IsNullOrWhiteSpace(value))
                {
                    missing ??= name;
                    return match.Value;
                }

                return Uri.EscapeDataString(value);
            });

            if (missing != null)
            {
                _logger.LogWarning(
                    "Proxy node: path parameter {Parameter} is missing for proxy {Slug} route {Route}.",
                    missing, parameters.Slug, template);
                return (string.Empty, $"Path parameter '{missing}' has no value for endpoint '{template}'.");
            }

            return (resolved, null);
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

            try
            {
                JsonDocument.Parse(bodyContent);
            }
            catch (JsonException ex)
            {
                _logger.LogError("Proxy node: body is not valid JSON for proxy {Slug}.", parameters.Slug);
                return (null, null, $"Invalid JSON body: {ex.Message}");
            }

            return (Encoding.UTF8.GetBytes(bodyContent), "application/json", null);
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
        /// surfacing a bare status code. The two failures a saved node is most likely to hit are an
        /// endpoint removed from the allowlist and a method no longer accepted, so both name what the
        /// proxy allows now.
        /// </summary>
        private static string DescribeFailure(
            ActionProxyParameters parameters, string method, ProxyForwardResult result)
        {
            var slug = parameters.Slug;
            var route = string.IsNullOrEmpty(parameters.RoutePath) ? "/" : parameters.RoutePath;
            var detail = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : $" {result.ErrorMessage}";

            if (result.Outcome == ProxyExecutionOutcome.RouteNotAllowed)
            {
                return $"Proxy '{slug}' does not declare the endpoint {method} {route}. "
                     + "It may have been removed from the proxy's allowed routes; re-select the endpoint on this node.";
            }

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

            return $"Proxy '{slug}' call to {method} {route} failed with {result.Outcome} ({result.StatusCode}).{upstream}{detail}";
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
    }
}
