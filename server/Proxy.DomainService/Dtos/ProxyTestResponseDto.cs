namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// 200 body of <c>POST /api/Proxy/Test</c> (SPEC &sect;3.3): the result of running the identical forward
    /// pipeline against a saved proxy or a draft, WITHOUT writing a <c>ProxyExecutions</c> row and WITHOUT
    /// touching any <c>Proxies</c> / <c>ProxyVersions</c> data.
    /// </summary>
    public sealed class ProxyTestResponseDto
    {
        /// <summary><c>true</c> iff a response was relayed (<c>outcome == "Success"</c>).</summary>
        public bool Ok { get; set; }

        /// <summary>Status relayed to the caller / mapped error status.</summary>
        public int Status { get; set; }

        /// <summary>Same enum as <c>ProxyExecutionEntity.Outcome</c>.</summary>
        public string Outcome { get; set; } = string.Empty;

        public int LatencyMs { get; set; }

        /// <summary>Rebuilt URL; secret-ref values keep their <c>${SECRET.NAME}</c> token.</summary>
        public string UpstreamUrl { get; set; } = string.Empty;

        public string UpstreamHost { get; set; } = string.Empty;

        public List<string> InjectedHeaderKeys { get; set; } = new();

        public List<string> InjectedQueryKeys { get; set; } = new();

        public string? ResponseContentType { get; set; }

        /// <summary>Full upstream body (same capture seam as the persisted rows).</summary>
        public string? ResponseBody { get; set; }

        /// <summary>
        /// <c>true</c> iff a response filter ran and produced output (Applied or EmptyResult). <c>false</c>
        /// when the mode was All, the response was a whole-primitive, or the filter failed.
        /// </summary>
        public bool ResponseFilterApplied { get; set; }

        /// <summary><c>null</c> | <c>"Applied"</c> | <c>"EmptyResult"</c> | <c>"WholePrimitive"</c> | <c>"Failed"</c>.</summary>
        public string? ResponseFilterNote { get; set; }

        /// <summary>Size of the body relayed to the client, in bytes (post-projection under Select).</summary>
        public long ResponseBodyBytes { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
