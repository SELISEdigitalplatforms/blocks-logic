namespace Proxy.DomainService.Dtos
{
    /// <summary>Query string of <c>GET /api/Proxies/{proxyId}/executions/export</c> (SPEC &sect;3.4).</summary>
    public sealed class ProxyExportExecutionsRequestDto
    {
        /// <summary>Required. The proxy whose last-24 h log to export.</summary>
        public string? ProxyId { get; set; }

        /// <summary><c>all</c> (default) | <c>2xx</c> | <c>4xx</c> | <c>5xx</c>. Anything else is a 400 (C1).</summary>
        public string? StatusClass { get; set; }
    }
}
