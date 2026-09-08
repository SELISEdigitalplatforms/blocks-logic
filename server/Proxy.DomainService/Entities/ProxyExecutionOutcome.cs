namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// The closed set of <see cref="ProxyExecutionEntity.Outcome"/> values (SPEC &sect;3.1). Stored as a string
    /// so the <em>Request logs</em> tab (Phase 3) can group on it without a schema migration.
    /// <list type="bullet">
    /// <item><see cref="Success"/> &mdash; Blocks completed the round-trip and relayed the upstream response,
    /// regardless of its status class (a <c>500</c> from the vendor is still <see cref="Success"/>).</item>
    /// <item><see cref="Timeout"/> / <see cref="UpstreamUnreachable"/> &mdash; Blocks never got a response.</item>
    /// <item><see cref="UpstreamBlocked"/> &mdash; the resolved upstream address is private / loopback /
    /// link-local and was refused (SSRF guard).</item>
    /// <item><see cref="UpstreamResponseTooLarge"/> &mdash; the upstream response body exceeded the 10 MB cap.</item>
    /// <item><see cref="RequestTooLarge"/> / <see cref="MethodNotAllowed"/> / <see cref="ProxyNotFound"/> /
    /// <see cref="Unauthorized"/> &mdash; Blocks rejected the call before forwarding.</item>
    /// <item><see cref="InternalError"/> &mdash; an unexpected bug in the forwarder.</item>
    /// </list>
    /// </summary>
    public static class ProxyExecutionOutcome
    {
        public const string Success = "Success";
        public const string UpstreamError = "UpstreamError";
        public const string Timeout = "Timeout";
        public const string UpstreamUnreachable = "UpstreamUnreachable";
        public const string UpstreamBlocked = "UpstreamBlocked";
        public const string UpstreamResponseTooLarge = "UpstreamResponseTooLarge";
        public const string RequestTooLarge = "RequestTooLarge";
        public const string MethodNotAllowed = "MethodNotAllowed";
        public const string ProxyNotFound = "ProxyNotFound";
        public const string Unauthorized = "Unauthorized";
        public const string InternalError = "InternalError";
    }
}
