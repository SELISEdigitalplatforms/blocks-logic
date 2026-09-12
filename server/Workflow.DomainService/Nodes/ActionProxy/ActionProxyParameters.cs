namespace Workflow.DomainService.Nodes.ActionProxy
{
    /// <summary>
    /// Parameters for the Proxy action node. The node names a proxy by slug and lets the Proxy domain
    /// own everything else: the upstream URL, the injected headers and query parameters, secret
    /// resolution, and response shaping. Nothing here carries an upstream credential.
    /// </summary>
    public class ActionProxyParameters
    {
        /// <summary>Proxy <c>ItemId</c>, kept for traceability. The forward resolves by slug.</summary>
        public string ProxyId { get; set; } = string.Empty;

        /// <summary>Proxy slug, resolved against the run's tenant by the gateway service.</summary>
        public string Slug { get; set; } = string.Empty;

        /// <summary>
        /// Comma-separated methods the picked proxy allowed when the node was configured. Advisory only:
        /// the gateway re-checks and is the authority, since the proxy may have changed since.
        /// </summary>
        public string AllowedMethods { get; set; } = string.Empty;

        /// <summary>Upper-case HTTP method to forward.</summary>
        public string HttpMethod { get; set; } = "GET";

        /// <summary>Path appended after the upstream, with or without a leading slash. Supports expressions.</summary>
        public string Path { get; set; } = string.Empty;

        public bool HaveBody { get; set; } = false;

        public string BodyContentType { get; set; } = "json";

        /// <summary>JSON body forwarded to the upstream. Supports expressions.</summary>
        public string Body { get; set; } = string.Empty;
    }
}
