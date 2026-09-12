namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Body of <c>PUT /api/Proxies/{proxyId}</c>. Replaces name / upstream / methods / headers / query only.
    /// <c>Slug</c> is immutable and not accepted; a <c>slug</c> field in the payload is ignored.
    /// <c>Enabled</c> is unchanged by Update (use Toggle).
    /// </summary>
    public sealed class ProxyUpdateRequestDto
    {
        public string? ItemId { get; set; }

        public string? Name { get; set; }

        public string? Upstream { get; set; }

        public List<string>? Methods { get; set; }

        public List<ProxyKeyValueInputDto>? Headers { get; set; }

        public List<ProxyKeyValueInputDto>? Query { get; set; }

        /// <summary>
        /// Fields merged into the top level of the client's JSON body on POST / PUT / PATCH forwards.
        /// Empty / omitted ⇒ the body is forwarded unchanged.
        /// </summary>
        public List<ProxyKeyValueInputDto>? BodyMerge { get; set; }

        /// <summary>
        /// Per-method overrides. Superseded by <see cref="Routes"/>, which carry their own method; the console
        /// no longer writes this and sends it empty. Still accepted and applied so stored values keep working.
        /// </summary>
        public List<ProxyMethodConfigInputDto>? MethodConfigs { get; set; }

        /// <summary>
        /// The endpoints this proxy may reach. Omitted / empty ⇒ the proxy accepts its base path only and
        /// any path suffix is refused, which keeps it one-to-one with a single upstream endpoint.
        /// </summary>
        public List<ProxyRouteConfigInputDto>? Routes { get; set; }

        /// <summary><c>"All"</c> (default) or <c>"Select"</c>. See <c>ProxyResponseProjector</c>.</summary>
        public string? ResponseMode { get; set; }

        /// <summary>Response field paths kept when <see cref="ResponseMode"/> is <c>"Select"</c>.</summary>
        public List<string>? ResponseInclude { get; set; }
    }
}
