namespace Proxy.DomainService.Dtos
{
    /// <summary>Route id of <c>GET /api/Proxies/{proxyId}/overview</c> (SPEC &sect;3.3) &mdash; the Overview-tab tiles.</summary>
    public sealed class ProxyGetOverviewRequestDto
    {
        /// <summary>Required. The proxy whose rolling 24 h metrics to compute.</summary>
        public string? ProxyId { get; set; }
    }
}
