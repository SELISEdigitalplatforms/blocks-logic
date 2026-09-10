namespace Proxy.DomainService.Dtos
{
    /// <summary>Query string of <c>DELETE /api/Proxy/Delete</c>.</summary>
    public sealed class ProxyDeleteRequestDto
    {
        public string? ItemId { get; set; }
    }
}
