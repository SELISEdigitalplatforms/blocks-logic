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

        /// <summary>
        /// Who can call the gateway route. Omitted ⇒ reset to the default (Blocks token, any signed-in
        /// caller), so the console must always send the current value back on Update.
        /// </summary>
        public ProxyResilienceInputDto? Resilience { get; set; }

        /// <summary>
        /// Gateway calls per minute for this proxy, from all callers together (P-3). <c>null</c> ⇒ the default:
        /// 600 for a Public proxy, no limit for a token one. 1..100000. On Update, omitted resets it to the
        /// default, so the console always sends the current value back.
        /// </summary>
        public int? RequestsPerMinute { get; set; }

        public ProxyAccessInputDto? Access { get; set; }

        /// <summary>
        /// The <c>currentVersion</c> the console loaded (PX-16). When the proxy has moved on since, the write is
        /// refused with 409 <c>PROXY_VERSION_CONFLICT</c> instead of silently overwriting the other change.
        /// <c>null</c> (old clients) ⇒ only the read-then-write race on the server is guarded.
        /// </summary>
        public int? ExpectedVersion { get; set; }
    }
}
