using System.Text.Json.Serialization;
using Blocks.Genesis;

namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Response of <c>POST /api/Proxy/GetOverview</c>. <c>Data</c> carries the rolling 24 h metrics; on an
    /// unknown proxy that also has no execution rows, <see cref="HttpStatus"/> is 404 and <see cref="Code"/>
    /// is <c>PROXY_NOT_FOUND</c> (SPEC &sect;3.3 / C2). A missing <c>proxyId</c> is a 400 (<c>PROXY_VALIDATION</c>).
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
        /// <summary>Count of rows in the window.</summary>
        public long Calls24h { get; set; }

        /// <summary><c>round(mean(LatencyMs))</c> over the window; <c>0</c> when <see cref="Calls24h"/> is 0.</summary>
        public int AvgLatencyMs { get; set; }

        /// <summary><c>round(errorShare * 100, 1)</c> where an error is <c>StatusCode &gt;= 400</c>; <c>0</c> when empty.</summary>
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

        /// <summary><c>StartedAtUtc</c> of the most recent row in the window, or <c>null</c> when empty.</summary>
        public DateTime? LastCallAtUtc { get; set; }
    }
}
