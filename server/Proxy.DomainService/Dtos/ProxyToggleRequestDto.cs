namespace Proxy.DomainService.Dtos
{
    /// <summary>Body of <c>PATCH /api/Proxies/{proxyId}</c>.</summary>
    public sealed class ProxyToggleRequestDto
    {
        public string? ItemId { get; set; }

        public bool Enabled { get; set; }
    }
}
