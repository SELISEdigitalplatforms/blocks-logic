namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// One declared endpoint as returned to the console. A <c>null</c> member means the route inherits the
    /// proxy's shared value, which the form renders as "inherited" rather than as an empty override.
    /// </summary>
    public sealed class ProxyRouteConfigDto
    {
        public string Method { get; set; } = string.Empty;

        /// <summary>Client-facing path template, with no leading slash. <c>""</c> is the base path.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary><c>null</c> when the route does not rewrite.</summary>
        public string? UpstreamPath { get; set; }

        public List<ProxyKeyValueDto>? Headers { get; set; }

        public List<ProxyKeyValueDto>? Query { get; set; }

        public List<ProxyKeyValueDto>? BodyMerge { get; set; }

        public string? ResponseMode { get; set; }

        public List<string>? ResponseInclude { get; set; }
    }
}
