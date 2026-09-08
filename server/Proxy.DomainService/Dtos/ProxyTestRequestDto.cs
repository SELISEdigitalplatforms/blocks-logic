namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Body of <c>POST /api/Proxy/Test</c> (SPEC &sect;3.3). Exactly one of <see cref="ProxyId"/> /
    /// <see cref="Draft"/> must be present: reference a saved proxy, or supply a full draft used before the
    /// first save in the console form.
    /// </summary>
    public sealed class ProxyTestRequestDto
    {
        /// <summary>Id of a saved proxy for the caller's tenant. Mutually exclusive with <see cref="Draft"/>.</summary>
        public string? ProxyId { get; set; }

        /// <summary>An unsaved proxy configuration. Mutually exclusive with <see cref="ProxyId"/>.</summary>
        public ProxyTestDraftDto? Draft { get; set; }

        /// <summary>One of the effective methods; required.</summary>
        public string? Method { get; set; }

        /// <summary>Appended like <c>{**path}</c>; default <c>""</c>.</summary>
        public string? PathSuffix { get; set; }

        /// <summary>Extra raw query string; default <c>""</c>.</summary>
        public string? Query { get; set; }

        /// <summary>Request body; default none. The 10 MB cap still applies.</summary>
        public string? Body { get; set; }

        /// <summary>Defaults to <c>application/json</c> when <see cref="Body"/> is present.</summary>
        public string? ContentType { get; set; }
    }

    /// <summary>An unsaved proxy configuration supplied to <c>POST /api/Proxy/Test</c>.</summary>
    public sealed class ProxyTestDraftDto
    {
        public string? Upstream { get; set; }

        public List<string>? Methods { get; set; }

        public List<ProxyKeyValueInputDto>? Headers { get; set; }

        public List<ProxyKeyValueInputDto>? Query { get; set; }

        /// <summary>
        /// Fields merged into the top level of the client's JSON body on POST / PUT / PATCH forwards.
        /// Same rules as Create / Update.
        /// </summary>
        public List<ProxyKeyValueInputDto>? BodyMerge { get; set; }

        /// <summary>Per-method overrides for the unsaved draft. Same rules as Create / Update.</summary>
        public List<ProxyMethodConfigInputDto>? MethodConfigs { get; set; }
    }
}
