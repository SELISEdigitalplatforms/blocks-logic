using System.Text.Json.Serialization;
using Blocks.Genesis;

namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Response of <c>GET /api/Proxies/{proxyId}/overview</c>. <c>Data</c> carries <see cref="ProxyOverviewDto.Calls24h"/>
    /// over a rolling 24 h window and <see cref="ProxyOverviewDto.AvgLatencyMs"/> / <see cref="ProxyOverviewDto.ErrorRatePct"/>
    /// over all-time rows; on an unknown proxy that also has no execution rows, <see cref="HttpStatus"/> is 404
    /// and <see cref="Code"/> is <c>PROXY_NOT_FOUND</c> (SPEC &sect;3.3 / C2). A missing <c>proxyId</c> is a 400
    /// (<c>PROXY_VALIDATION</c>).
    /// </summary>
    public sealed class ProxyGetOverviewResponseDto : BaseQueryResponse<ProxyOverviewDto?>
    {
        public string? Code { get; set; }

        public string? Message { get; set; }

        [JsonIgnore]
        public int HttpStatus { get; set; } = 200;
    }

    /// <summary>The Overview tiles plus the Configuration-panel convenience fields (SPEC &sect;3.3).</summary>
    public sealed class ProxyOverviewDto
    {
        /// <summary>Count of rows in the rolling 24h window.</summary>
        public long Calls24h { get; set; }

        /// <summary>
        /// <c>round(mean(LatencyMs))</c> over all-time rows for this proxy, not just <see cref="Calls24h"/>'s
        /// window; <c>0</c> when there are no calls at all.
        /// </summary>
        public int AvgLatencyMs { get; set; }

        /// <summary>
        /// <c>round(errorShare * 100, 1)</c> over all-time rows, where an error is <c>StatusCode &gt;= 400</c>;
        /// <c>0</c> when there are no calls at all.
        /// </summary>
        public double ErrorRatePct { get; set; }

        /// <summary><see cref="ErrorRatePct"/> &gt; 5 (drives the red tile in the mock).</summary>
        public bool ErrorRateIsHigh { get; set; }

        /// <summary>
        /// Distinct <c>${SECRET.NAME}</c> tokens found across the proxy's Headers+Query values (from the
        /// <c>Proxies</c> row, not executions); empty when none or when the proxy has been deleted.
        /// </summary>
        public List<string> CredentialRefs { get; set; } = new();

        /// <summary><c>proxy.Methods</c> (convenience for the panel); empty when the proxy has been deleted.</summary>
        public List<string> Methods { get; set; } = new();

        /// <summary><c>StartedAtUtc</c> of the most recent row for this proxy (all-time), or <c>null</c> when empty.</summary>
        public DateTime? LastCallAtUtc { get; set; }
    }
}
