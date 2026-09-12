using System.Text.Json.Serialization;
using Blocks.Genesis;

namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Response of <c>GET /api/Proxies/{proxyId}/executions</c>. <c>TotalCount</c> is the unpaged 24 h match count for
    /// the requested <c>statusClass</c> and is NOT affected by <c>afterId</c> (SPEC &sect;3.1 / H2, H3). On a
    /// validation failure <see cref="HttpStatus"/> is 400 and <see cref="Code"/> is <c>PROXY_VALIDATION</c>;
    /// on an unknown proxy it is 404 / <c>PROXY_NOT_FOUND</c>.
    /// </summary>
    public sealed class ProxyGetExecutionsResponseDto : BaseQueryListResponse<List<ProxyExecutionListItemDto>>
    {
        /// <summary>
        /// The window top this response was computed against. Send it back as <c>asOfUtc</c> on the next page
        /// request to keep paging over a stable set of rows; drop it to re-pin to "now" (a filter change, or a
        /// deliberate refresh). <c>null</c> on a validation / not-found response.
        /// </summary>
        public DateTime? AsOfUtc { get; set; }

        /// <summary>Stable failure code (see <see cref="Utils.ProxyErrorCodes"/>); <c>null</c> on success.</summary>
        public string? Code { get; set; }

        /// <summary>Human-readable message for the 404 case; <c>null</c> otherwise.</summary>
        public string? Message { get; set; }

        /// <summary>HTTP status the controller returns (200 normally, 400 on validation, 404 on unknown proxy).</summary>
        [JsonIgnore]
        public int HttpStatus { get; set; } = 200;
    }

    /// <summary>One row of the <em>Request logs</em> list (SPEC &sect;3.1).</summary>
    public sealed class ProxyExecutionListItemDto
    {
        public string ItemId { get; set; } = string.Empty;

        public DateTime StartedAtUtc { get; set; }

        public string RequestMethod { get; set; } = string.Empty;

        public string RequestPath { get; set; } = string.Empty;

        public int StatusCode { get; set; }

        public int LatencyMs { get; set; }

        /// <summary>One of <see cref="Entities.ProxyExecutionOutcome"/>.</summary>
        public string Outcome { get; set; } = string.Empty;

        public string UpstreamHost { get; set; } = string.Empty;

        /// <summary><c>"Client"</c>, <c>"Workflow"</c> or <c>"Test"</c> — where the call came from.</summary>
        public string CallerKind { get; set; } = string.Empty;

        /// <summary>Display name of the calling user at call time, or <c>null</c>.</summary>
        public string? CallerUserName { get; set; }

        /// <summary>The route template that matched, or <c>null</c> when the call was rejected before matching.</summary>
        public string? RoutePath { get; set; }
    }
}
