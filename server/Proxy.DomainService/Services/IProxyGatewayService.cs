namespace Proxy.DomainService.Services
{
    /// <summary>
    /// The forward engine (data plane). Given an authenticated <see cref="ProxyForwardRequest"/> it resolves
    /// the proxy, rebuilds the request against the stored upstream, attaches ONLY the configured headers and
    /// query parameters, calls the third party server-side (buffered, 30 s timeout, no redirects), and — for
    /// non-test calls — writes exactly one <see cref="Entities.ProxyExecutionEntity"/> (including failures and
    /// pre-flight rejections). It never throws for an upstream or persistence failure; the failure is encoded
    /// in the returned <see cref="ProxyForwardResult"/>.
    /// </summary>
    public interface IProxyGatewayService
    {
        /// <summary>
        /// Loads the proxy behind <paramref name="slug"/> for <paramref name="tenantId"/> as the forwarder's
        /// resolved view, or <c>null</c> when no such proxy exists. The controller calls this first so it can
        /// enforce the proxy's <see cref="ProxyResolvedConfig.Access"/> policy, then hands the same config to
        /// <see cref="ForwardAsync"/> via <see cref="ProxyForwardRequest.ResolvedConfig"/> so the row is read once.
        /// </summary>
        Task<ProxyResolvedConfig?> ResolveAsync(string tenantId, string slug, CancellationToken cancellationToken = default);

        Task<ProxyForwardResult> ForwardAsync(ProxyForwardRequest request, CancellationToken cancellationToken = default);
    }
}
