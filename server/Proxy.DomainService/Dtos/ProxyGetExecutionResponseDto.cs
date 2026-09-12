using System.Text.Json.Serialization;
using Blocks.Genesis;

namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Response of <c>GET /api/Proxies/{proxyId}/executions/{executionId}</c>. <c>Data</c> is <c>null</c> for an unknown id, an id
    /// whose <c>ProxyId</c> differs, or a row from another tenant (SPEC &sect;3.2 / C3) &mdash; never a 404.
    /// A missing <c>itemId</c>/<c>proxyId</c> is the only 400 (<see cref="Code"/> <c>PROXY_VALIDATION</c>).
    /// </summary>
    public sealed class ProxyGetExecutionResponseDto : BaseQueryResponse<ProxyExecutionDetailDto?>
    {
        public string? Code { get; set; }

        public string? Message { get; set; }

        [JsonIgnore]
        public int HttpStatus { get; set; } = 200;
    }

    /// <summary>Full per-call detail for the expanded request-log row (SPEC &sect;3.2).</summary>
    public sealed class ProxyExecutionDetailDto
    {
        public string ItemId { get; set; } = string.Empty;

        public string ProxyId { get; set; } = string.Empty;

        public string ProxySlug { get; set; } = string.Empty;

        public DateTime StartedAtUtc { get; set; }

        public DateTime FinishedAtUtc { get; set; }

        public int LatencyMs { get; set; }

        public string RequestMethod { get; set; } = string.Empty;

        public string RequestPath { get; set; } = string.Empty;

        public string RequestQuery { get; set; } = string.Empty;

        /// <summary>Rebuilt absolute URL; secret-ref values keep their <c>${SECRET.NAME}</c> token.</summary>
        public string UpstreamUrl { get; set; } = string.Empty;

        public string UpstreamHost { get; set; } = string.Empty;

        public List<string> InjectedHeaderKeys { get; set; } = new();

        public List<string> InjectedQueryKeys { get; set; } = new();

        /// <summary><c>"Client"</c>, <c>"Workflow"</c> or <c>"Test"</c>.</summary>
        public string CallerKind { get; set; } = string.Empty;

        /// <summary>Calling user id; <c>null</c> when the credential carried none.</summary>
        public string? CallerUserId { get; set; }

        public string? CallerUserName { get; set; }

        /// <summary>Remote IP of the caller; <c>null</c> for an in-process workflow forward.</summary>
        public string? CallerIp { get; set; }

        public string? CallerUserAgent { get; set; }

        public string? CallerOrigin { get; set; }

        /// <summary>Trace id joining this row to the surrounding application logs.</summary>
        public string? CorrelationId { get; set; }

        /// <summary>Set only when <see cref="CallerKind"/> is <c>"Workflow"</c>.</summary>
        public string? WorkflowId { get; set; }

        public string? WorkflowRunId { get; set; }

        public string? WorkflowNodeId { get; set; }

        /// <summary>Client-facing route template that matched, e.g. <c>"orders/{id}"</c>.</summary>
        public string? RoutePath { get; set; }

        /// <summary>Upstream template it rewrote to, or <c>null</c> when the route did not rewrite.</summary>
        public string? RouteUpstreamPath { get; set; }

        public int StatusCode { get; set; }

        public int? UpstreamStatusCode { get; set; }

        public string Outcome { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }

        public string? ResponseContentType { get; set; }

        /// <summary>
        /// <c>true</c> iff a response filter ran and produced output (Applied or EmptyResult) for this call.
        /// </summary>
        public bool ResponseFilterApplied { get; set; }

        /// <summary><c>null</c> | <c>"Applied"</c> | <c>"EmptyResult"</c> | <c>"WholePrimitive"</c> | <c>"Failed"</c>.</summary>
        public string? ResponseFilterNote { get; set; }

        public long ResponseBodyBytes { get; set; }

        /// <summary>
        /// The stored upstream body, clipped to the API display limit (64 KB) when longer. The stored row is
        /// never modified; see <see cref="ResponseBodyTruncatedForDisplay"/>.
        /// </summary>
        public string? ResponseBody { get; set; }

        /// <summary><c>true</c> iff the API clipped <see cref="ResponseBody"/> before returning it (C6).</summary>
        public bool ResponseBodyTruncatedForDisplay { get; set; }
    }
}
