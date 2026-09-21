namespace Common.InternalService.Access
{
    /// <summary>
    /// Who may call a tenant-published endpoint (a proxy gateway route, a webhook, a function URL).
    /// Persisted as a string so the console can add kinds without a schema migration.
    /// </summary>
    public enum EndpointAccessKind
    {
        /// <summary>
        /// The caller must present a Blocks bearer token for the tenant. Identity, roles and permissions
        /// are available to the endpoint, and <see cref="EndpointAccessPolicy.Roles"/> /
        /// <see cref="EndpointAccessPolicy.Permissions"/> may narrow the set of callers further.
        /// This is the default: an endpoint never becomes public by omission.
        /// </summary>
        BlocksToken = 0,

        /// <summary>Anyone with the URL can call it. No identity, no token-scoped work.</summary>
        Public = 1,
    }
}
