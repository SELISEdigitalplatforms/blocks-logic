namespace Proxy.DomainService.Dtos
{
    /// <summary>Query string of <c>GET /api/Proxies</c>. All fields optional; paging is clamped by the service.</summary>
    public sealed class ProxyGetAllRequestDto
    {
        /// <summary>Case-insensitive substring match on name and slug.</summary>
        public string? Search { get; set; }

        /// <summary>
        /// Alias of <see cref="Search"/>. Functions and Scheduler lists use <c>searchKey</c>, so callers often
        /// send it here too; without this alias it was silently ignored and the list came back unfiltered.
        /// </summary>
        public string? SearchKey { get; set; }

        /// <summary>The search term to apply: <see cref="Search"/> wins when both are sent; blank means none.</summary>
        public string? GetSearchTerm() =>
            !string.IsNullOrWhiteSpace(Search) ? Search : string.IsNullOrWhiteSpace(SearchKey) ? null : SearchKey;

        /// <summary>When set, restricts to active (<c>true</c>) or inactive (<c>false</c>) proxies.</summary>
        public bool? IsActive { get; set; }

        /// <summary>1..200, default 20.</summary>
        public int PageSize { get; set; } = 20;

        /// <summary>Zero-based, default 0.</summary>
        public int PageNumber { get; set; }
    }
}
