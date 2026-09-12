namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// One declared endpoint as submitted by the console on Create / Update. Omitting a member (leaving it
    /// <c>null</c>) inherits the proxy's shared value; supplying one overrides it for this route only.
    /// </summary>
    public sealed class ProxyRouteConfigInputDto
    {
        /// <summary>Wire name of the method this route accepts. Must be one the proxy allows.</summary>
        public string? Method { get; set; }

        /// <summary>Client-facing path template, e.g. <c>"orders/{id}"</c>. Blank / omitted ⇒ the base path.</summary>
        public string? Path { get; set; }

        /// <summary>
        /// Path template appended to the upstream, e.g. <c>"v1/charges/{id}"</c>. Omitted ⇒ no rewrite;
        /// <see cref="Path"/> is used verbatim. May only use parameters <see cref="Path"/> declares.
        /// </summary>
        public string? UpstreamPath { get; set; }

        public List<ProxyKeyValueInputDto>? Headers { get; set; }

        public List<ProxyKeyValueInputDto>? Query { get; set; }

        /// <summary>
        /// Body fields merged on POST / PUT / PATCH for this route. An explicitly empty array overrides the
        /// proxy-wide merge with "merge nothing" — how a route opts out of fields its payload must not carry.
        /// </summary>
        public List<ProxyKeyValueInputDto>? BodyMerge { get; set; }

        /// <summary><c>"All"</c> or <c>"Select"</c>. Omitted ⇒ inherit the proxy's mode.</summary>
        public string? ResponseMode { get; set; }

        /// <summary>Response field paths kept for this route. Omitted ⇒ inherit the proxy's list.</summary>
        public List<string>? ResponseInclude { get; set; }
    }
}
