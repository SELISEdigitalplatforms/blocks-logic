namespace Proxy.DomainService.Dtos
{
    /// <summary>Body of <c>POST /api/Proxies</c>.</summary>
    public sealed class ProxyCreateRequestDto
    {
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

        /// <summary>Per-method overrides. Reserved for Phase D-feature; a non-empty value is rejected today.</summary>
        public List<ProxyMethodConfigInputDto>? MethodConfigs { get; set; }

        /// <summary><c>"All"</c> (default) or <c>"Select"</c>. See <c>ProxyResponseProjector</c>.</summary>
        public string? ResponseMode { get; set; }

        /// <summary>Response field paths kept when <see cref="ResponseMode"/> is <c>"Select"</c>.</summary>
        public List<string>? ResponseInclude { get; set; }

        /// <summary>Defaults to <c>true</c> when omitted.</summary>
        public bool Enabled { get; set; } = true;
    }
}
