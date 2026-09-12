using Blocks.Genesis;

namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Response of <c>GET /api/Proxies</c>. <c>TotalCount</c> is the unpaged match count for the filter.
    /// </summary>
    public sealed class ProxyGetAllResponseDto : BaseQueryListResponse<List<ProxyListItemDto>>
    {
    }

    /// <summary>One row of the console's proxy list.</summary>
    public sealed class ProxyListItemDto
    {
        public string ItemId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        /// <summary>Host with middle labels obscured + <c>/&#8226;&#8226;&#8226;</c> (see <see cref="Utils.ProxyUpstreamMasker"/>).</summary>
        public string UpstreamMasked { get; set; } = string.Empty;

        public List<string> Methods { get; set; } = new();

        public bool Enabled { get; set; }

        /// <summary><c>true</c> when any header / query entry holds a <c>${SECRET.NAME}</c> reference.</summary>
        public bool InjectedCredential { get; set; }

        public int HeaderCount { get; set; }

        public int QueryCount { get; set; }

        /// <summary>
        /// Count of <c>ProxyExecutions</c> rows for this proxy with <c>StartedAtUtc &gt;= UtcNow-24h</c>
        /// (SPEC3 &sect;3.5). Computed for the whole page in one grouped aggregation; <c>0</c> when the proxy
        /// has no executions in the window or the aggregation failed (C9).
        /// </summary>
        public long Calls24h { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime LastUpdatedDate { get; set; }
    }
}
