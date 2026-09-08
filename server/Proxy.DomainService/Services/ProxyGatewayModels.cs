using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// The already-resolved proxy configuration a forward runs against. Supplied by the <c>Test</c> service
    /// (from a saved proxy or an unsaved draft); left <c>null</c> by the data-plane controller so
    /// <see cref="IProxyGatewayService"/> loads it by <c>(TenantId, Slug)</c> itself.
    /// </summary>
    public sealed class ProxyResolvedConfig
    {
        /// <summary><see cref="ProxyDetailEntity.ItemId"/>, or <c>""</c> for an unsaved draft.</summary>
        public string ProxyId { get; init; } = string.Empty;

        public string Slug { get; init; } = string.Empty;

        public required string Upstream { get; init; }

        public IReadOnlyList<HttpMethodType> Methods { get; init; } = Array.Empty<HttpMethodType>();

        public bool Enabled { get; init; } = true;

        public IReadOnlyList<ProxyKeyValue> Headers { get; init; } = Array.Empty<ProxyKeyValue>();

        public IReadOnlyList<ProxyKeyValue> Query { get; init; } = Array.Empty<ProxyKeyValue>();

        /// <summary>
        /// Fields merged into the top level of the client's JSON body when forwarding POST / PUT / PATCH.
        /// Empty ⇒ the body is forwarded unchanged.
        /// </summary>
        public IReadOnlyList<ProxyKeyValue> BodyMerge { get; init; } = Array.Empty<ProxyKeyValue>();

        /// <summary>
        /// Per-method overrides. Empty ⇒ every method uses the shared <see cref="Headers"/> / <see cref="Query"/>
        /// / <see cref="Upstream"/> (today's behaviour). Not writable via the API until Phase D-feature.
        /// </summary>
        public IReadOnlyList<ProxyMethodConfig> MethodConfigs { get; init; } = Array.Empty<ProxyMethodConfig>();

        public static ProxyResolvedConfig FromEntity(ProxyDetailEntity proxy) => new()
        {
            ProxyId = proxy.ItemId,
            Slug = proxy.Slug,
            Upstream = proxy.Upstream,
            Methods = proxy.Methods,
            Enabled = proxy.Enabled,
            Headers = proxy.Headers,
            Query = proxy.Query,
            BodyMerge = proxy.BodyMerge,
            MethodConfigs = proxy.MethodConfigs,
        };
    }

    /// <summary>Everything <see cref="IProxyGatewayService.ForwardAsync"/> needs for one attempt.</summary>
    public sealed class ProxyForwardRequest
    {
        public required string TenantId { get; init; }

        /// <summary>Calling user id when resolvable from the bearer, else <c>null</c> (&rarr; <c>CreatedBy</c>).</summary>
        public string? UserId { get; init; }

        /// <summary>Proxy slug from the route. Ignored when <see cref="ResolvedConfig"/> is supplied.</summary>
        public string Slug { get; init; } = string.Empty;

        /// <summary>Pre-resolved config (Test path). When <c>null</c> the service loads by slug.</summary>
        public ProxyResolvedConfig? ResolvedConfig { get; init; }

        /// <summary>Upper-case HTTP method received from the client.</summary>
        public required string Method { get; init; }

        /// <summary>Everything captured by <c>{**path}</c> (no leading slash), or <c>""</c>.</summary>
        public string PathSuffix { get; init; } = string.Empty;

        /// <summary>Raw incoming query string with no leading <c>?</c> (<c>""</c> when none).</summary>
        public string IncomingQuery { get; init; } = string.Empty;

        /// <summary>Client-facing path recorded on the row, e.g. <c>/api/proxy/gateway/slug/rest</c>.</summary>
        public string RequestPath { get; init; } = string.Empty;

        /// <summary>Request body already buffered by the caller, or <c>null</c> when there is none.</summary>
        public byte[]? Body { get; init; }

        /// <summary>Set by the caller when the inbound body exceeded the 10 MB cap (no upstream call is made).</summary>
        public bool BodyTooLarge { get; init; }

        /// <summary>Caller's <c>Content-Type</c> for the body, forwarded verbatim when a body is present.</summary>
        public string? ContentType { get; init; }

        /// <summary>
        /// <c>true</c> for <c>POST /api/Proxy/Test</c>: run the identical pipeline but write NO execution row.
        /// </summary>
        public bool IsTest { get; init; }
    }

    /// <summary>
    /// The result of a forward: enough to build both the caller-facing HTTP response and (for data-plane
    /// calls) the persisted <see cref="ProxyExecutionEntity"/>.
    /// </summary>
    public sealed class ProxyForwardResult
    {
        /// <summary>Status relayed to the caller (SPEC &sect;3.4).</summary>
        public int StatusCode { get; init; }

        /// <summary>One of <see cref="ProxyExecutionOutcome"/>.</summary>
        public string Outcome { get; init; } = ProxyExecutionOutcome.InternalError;

        /// <summary>The third party's status, or <c>null</c> when no response was received.</summary>
        public int? UpstreamStatusCode { get; init; }

        /// <summary>Rebuilt absolute URL (secret-ref values keep their <c>${SECRET.NAME}</c> token).</summary>
        public string UpstreamUrl { get; init; } = string.Empty;

        public string UpstreamHost { get; init; } = string.Empty;

        public IReadOnlyList<string> InjectedHeaderKeys { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> InjectedQueryKeys { get; init; } = Array.Empty<string>();

        public int LatencyMs { get; init; }

        /// <summary>Upstream body decoded as UTF-8 (via <see cref="Utils.ExecutionBodyStore.Capture"/>), or <c>null</c>.</summary>
        public string? ResponseBody { get; init; }

        public long ResponseBodyBytes { get; init; }

        public string? ResponseContentType { get; init; }

        public string? ErrorMessage { get; init; }

        /// <summary>Raw upstream bytes for a byte-for-byte relay. Never persisted; <c>null</c> unless Success.</summary>
        public byte[]? ResponseBytes { get; init; }

        /// <summary>Configured methods, for the <c>Allow</c> header on a 405.</summary>
        public IReadOnlyList<string> AllowedMethods { get; init; } = Array.Empty<string>();

        public DateTime StartedAtUtc { get; init; }

        public DateTime FinishedAtUtc { get; init; }

        /// <summary><c>true</c> iff a response was relayed (<see cref="Outcome"/> == <see cref="ProxyExecutionOutcome.Success"/>).</summary>
        public bool Ok => Outcome == ProxyExecutionOutcome.Success;
    }
}
