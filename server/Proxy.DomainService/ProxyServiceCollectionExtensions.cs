using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService
{
    /// <summary>
    /// Registers the Proxy control-plane services and repositories (Phase 1) plus the Phase 2 data-plane
    /// forwarder, its inbound-auth service, the <c>ProxyExecutions</c> repository, the
    /// <c>{{$VAR.name}}</c> configuration-variable resolver, and the named <c>"proxy-upstream"</c> <see cref="System.Net.Http.HttpClient"/>
    /// (30 s timeout, no redirect following). Phase 3 adds <see cref="IProxyExecutionService"/> (request
    /// logs / metrics / CSV export, read-only over <c>ProxyExecutions</c>) and a <see cref="TimeProvider"/>
    /// for its rolling 24h windows. Call from <c>Program.cs</c> next to
    /// <c>services.AddWorkflowExecutionEngine();</c>. Lifetimes mirror the Workflow domain (singletons).
    /// </summary>
    public static class ProxyServiceCollectionExtensions
    {
        public static IServiceCollection AddProxyServices(this IServiceCollection services)
        {
            // A pinnable clock for the rolling 24h windows in Phase 3 reads (kept injectable for tests).
            services.TryAddSingleton(TimeProvider.System);

            // business services
            services.AddSingleton<IProxyService, ProxyService>();
            services.AddSingleton<IProxyVersionService, ProxyVersionService>();
            services.AddSingleton<IProxyGatewayService, ProxyGatewayService>();
            services.AddSingleton<IProxyGatewayAuthService, ProxyGatewayAuthService>();
            services.AddSingleton<IProxyTestService, ProxyTestService>();
            services.AddSingleton<IProxyExecutionService, ProxyExecutionService>();

            // repositories
            services.AddSingleton<IProxyRepository, ProxyRepository>();
            services.AddSingleton<IProxyVersionRepository, ProxyVersionRepository>();
            services.AddSingleton<IProxyExecutionRepository, ProxyExecutionRepository>();

            // {{$VAR.name}} configuration-variable resolution: name -> id -> Key Vault value at forward time,
            // behind a short-TTL per-tenant cache. ISecretService is supplied by AddBlocksSecrets() in
            // Program.cs; if AddProxyServices() is ever used without it, this resolver throws at first use.
            services.AddMemoryCache();
            services.AddSingleton<IProxyVariableResolver, ProxyVariableResolver>();
            services.AddOptions<ProxyVariableResolverOptions>();

            // SSRF guard: rejects private / loopback / link-local upstream targets at config-write and again
            // (post-DNS) just before the send.
            services.AddSingleton<IProxyUpstreamGuard, ProxyUpstreamGuard>();

            // Denormalized traffic counters for the Overview tiles. The recorder must be a singleton: its
            // buffer IS the batching, and a scoped instance would flush one call at a time. The hosted
            // service drains it on a timer and once more on shutdown.
            services.AddSingleton<IProxyStatsRecorder, ProxyStatsRecorder>();

            services.AddHostedService<ProxyStatsFlushService>();

            // IHttpClientFactory + the buffered upstream client used by the forwarder.
            services.AddHttpClient();
            services.AddHttpClient(ProxyGatewayService.UpstreamClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);

                // Backstop for the manual response-size checks in the forwarder: HttpClient throws once the
                // buffered response passes this cap.
                client.MaxResponseContentBufferSize = ProxyGatewayService.MaxResponseBodyBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            });

            return services;
        }
    }
}
