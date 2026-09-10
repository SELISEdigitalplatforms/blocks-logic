namespace Proxy.DomainService.Dtos
{
    /// <summary>Body of <c>POST /api/Proxy/GetOverview</c> (SPEC &sect;3.3) &mdash; the Overview-tab tiles.</summary>
    public sealed class ProxyGetOverviewRequestDto
    {
        /// <summary>Required. The proxy whose rolling 24 h metrics to compute.</summary>
        public string? ProxyId { get; set; }
    }
}
