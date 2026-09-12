namespace Proxy.DomainService.Dtos
{
    /// <summary>Query string of <c>GET /api/Proxies</c>. All fields optional; paging is clamped by the service.</summary>
    public sealed class ProxyGetAllRequestDto
    {
        /// <summary>Case-insensitive substring match on name and slug.</summary>
        public string? Search { get; set; }

        /// <summary>When set, restricts to enabled (<c>true</c>) or disabled (<c>false</c>) proxies.</summary>
        public bool? Enabled { get; set; }

        /// <summary>1..200, default 20.</summary>
        public int PageSize { get; set; } = 20;

        /// <summary>Zero-based, default 0.</summary>
        public int PageNumber { get; set; }
    }
}
