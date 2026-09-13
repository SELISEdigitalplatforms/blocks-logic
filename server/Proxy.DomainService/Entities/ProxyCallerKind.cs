namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// The closed set of <see cref="ProxyExecutionEntity.CallerKind"/> values. Stored as a string so the
    /// <em>Request logs</em> tab can filter and group on it without a schema migration.
    /// </summary>
    public static class ProxyCallerKind
    {
        /// <summary>A front end (or any external client) calling <c>/api/proxy/gateway/{slug}/{**path}</c>.</summary>
        public const string Client = "Client";

        /// <summary>An in-process forward from a workflow's proxy node; no HTTP hop, no inbound credential.</summary>
        public const string Workflow = "Workflow";

        /// <summary>The console's Test action. Runs the whole pipeline but writes no execution row.</summary>
        public const string Test = "Test";
    }
}
