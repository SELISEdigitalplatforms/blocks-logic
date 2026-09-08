namespace Proxy.DomainService.Dtos
{
    /// <summary>Body of <c>POST /api/Proxy/GetVersions</c> (the Change history tab).</summary>
    public sealed class ProxyGetVersionsRequestDto
    {
        public string? ProxyId { get; set; }

        /// <summary>1..200, default 50.</summary>
        public int PageSize { get; set; } = 50;

        /// <summary>Zero-based, default 0.</summary>
        public int PageNumber { get; set; }
    }
}
