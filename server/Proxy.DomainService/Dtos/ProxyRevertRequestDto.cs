namespace Proxy.DomainService.Dtos
{
    /// <summary>Body of <c>POST /api/Proxy/Revert</c>.</summary>
    public sealed class ProxyRevertRequestDto
    {
        public string? ProxyId { get; set; }

        public string? VersionId { get; set; }
    }
}
