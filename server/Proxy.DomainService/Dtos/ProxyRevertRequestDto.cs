namespace Proxy.DomainService.Dtos
{
    /// <summary>Route ids of <c>POST /api/Proxies/{proxyId}/versions/{versionId}/revert</c>.</summary>
    public sealed class ProxyRevertRequestDto
    {
        public string? ProxyId { get; set; }

        public string? VersionId { get; set; }
    }
}
