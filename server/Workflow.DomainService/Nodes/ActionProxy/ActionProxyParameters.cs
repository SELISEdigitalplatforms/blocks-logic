namespace Workflow.DomainService.Nodes.ActionProxy
{
    /// <summary>
    /// Parameters for the Proxy action node. The node names a proxy and one of its declared routes and
    /// lets the Proxy domain own everything else: the upstream URL, the injected headers and query
    /// values, secret resolution, and response shaping. Nothing here carries an upstream credential.
    /// </summary>
    public class ActionProxyParameters
    {
        /// <summary>Proxy <c>ItemId</c>, kept for traceability. The forward resolves by slug.</summary>
        public string ProxyId { get; set; } = string.Empty;

        /// <summary>Proxy slug, resolved against the run's tenant by the gateway service.</summary>
        public string Slug { get; set; } = string.Empty;

        /// <summary>Method of the selected route. Upper-case.</summary>
        public string RouteMethod { get; set; } = string.Empty;

        /// <summary>
        /// The selected route's client-facing template, with no leading slash, exactly as the proxy
        /// declares it. <c>""</c> is the base path. e.g. <c>orders/{id}/refunds</c>.
        /// </summary>
        public string RoutePath { get; set; } = string.Empty;

        /// <summary>
        /// Values for the <c>{name}</c> segments of <see cref="RoutePath"/>, keyed by parameter name.
        /// Values support expressions and are resolved per input item.
        /// </summary>
        public Dictionary<string, string> PathParams { get; set; } = new();

        /// <summary>Whether the call carries query-string parameters.</summary>
        public bool HaveQuery { get; set; } = false;

        /// <summary>
        /// Query-string parameters sent with the call, keyed by name. Values support expressions and are
        /// resolved per input item. The proxy's own configured query values still override any key that
        /// collides, exactly as they do for a client call.
        /// </summary>
        public Dictionary<string, string> QueryParams { get; set; } = new();

        public bool HaveBody { get; set; } = false;

        /// <summary>JSON body forwarded to the upstream. Supports expressions.</summary>
        public string Body { get; set; } = string.Empty;
    }
}
