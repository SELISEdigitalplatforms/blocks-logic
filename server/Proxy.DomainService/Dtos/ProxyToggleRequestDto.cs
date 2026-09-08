namespace Proxy.DomainService.Dtos
{
    /// <summary>Body of <c>POST /api/Proxy/Toggle</c>.</summary>
    public sealed class ProxyToggleRequestDto
    {
        public string? ItemId { get; set; }

        public bool Enabled { get; set; }
    }
}
