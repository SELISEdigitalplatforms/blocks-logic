using Blocks.Genesis;

namespace Proxy.DomainService.Dtos
{
    /// <summary>Response of <c>GET /api/Proxies/{proxyId}</c>. <c>Data</c> is <c>null</c> for an unknown item id.</summary>
    public sealed class ProxyGetResponseDto : BaseQueryResponse<ProxyDetailDto?>
    {
    }

    /// <summary>Full proxy configuration for the console's Form view.</summary>
    public sealed class ProxyDetailDto
    {
        public string ItemId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        /// <summary>The client-facing gateway route from Phase 2: <c>/api/proxy/gateway/&lt;slug&gt;/*</c>.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>The full stored upstream URL (the console reveals it on demand).</summary>
        public string Upstream { get; set; } = string.Empty;

        public string UpstreamMasked { get; set; } = string.Empty;

        public List<string> Methods { get; set; } = new();

        public bool Enabled { get; set; }

        public List<ProxyKeyValueDto> Headers { get; set; } = new();

        public List<ProxyKeyValueDto> Query { get; set; } = new();

        /// <summary>
        /// Fields merged into the top level of the client's JSON body on POST / PUT / PATCH forwards.
        /// Empty ⇒ the body is forwarded unchanged.
        /// </summary>
        public List<ProxyKeyValueDto> BodyMerge { get; set; } = new();

        /// <summary>Per-method overrides. Always empty until Phase D-feature; wired so the form can round-trip it.</summary>
        public List<ProxyMethodConfigDto> MethodConfigs { get; set; } = new();

        /// <summary><c>"All"</c> or <c>"Select"</c> — how the upstream response body is treated on forward.</summary>
        public string ResponseMode { get; set; } = "All";

        /// <summary>Response field paths kept when <see cref="ResponseMode"/> is <c>"Select"</c>.</summary>
        public List<string> ResponseInclude { get; set; } = new();

        public int CurrentVersion { get; set; }

        public DateTime CreatedDate { get; set; }

        public string? CreatedBy { get; set; }

        public DateTime LastUpdatedDate { get; set; }

        public string? LastUpdatedBy { get; set; }
    }
}
