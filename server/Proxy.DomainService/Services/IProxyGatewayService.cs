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
        Task<ProxyForwardResult> ForwardAsync(ProxyForwardRequest request, CancellationToken cancellationToken = default);
    }
}
