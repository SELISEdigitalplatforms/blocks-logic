namespace Proxy.DomainService.Dtos
{
    /// <summary>Query string of <c>GET /api/Proxies/{proxyId}/versions</c> (the Change history tab).</summary>
    public sealed class ProxyGetVersionsRequestDto
    {
        public string? ProxyId { get; set; }

        /// <summary>1..200, default 50.</summary>
        public int PageSize { get; set; } = 50;

        /// <summary>Zero-based, default 0.</summary>
        public int PageNumber { get; set; }
    }
}
