using Microsoft.AspNetCore.Http;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Authenticates an inbound data-plane call to <c>/api/proxy/gateway/{slug}/{**path}</c>. The credential
    /// is the tenant <c>X-Blocks-Key</c> header plus a bearer token that must be valid for THAT tenant, so
    /// the controller is <c>[AllowAnonymous]</c> at the framework level and defers to this service. Reuses the
    /// token-validation approach of <c>WorkflowAuthService</c> (tenant lookup + cached public cert + JWT
    /// handler); it does NOT evaluate roles or permissions.
    /// </summary>
    public interface IProxyGatewayAuthService
    {
        Task<ProxyGatewayAuthResult> AuthenticateAsync(HttpRequest request);
    }

    /// <summary>Outcome of <see cref="IProxyGatewayAuthService.AuthenticateAsync"/>.</summary>
    public sealed class ProxyGatewayAuthResult
    {
        public bool IsAuthenticated { get; private init; }

        /// <summary>The tenant named by <c>X-Blocks-Key</c> (only meaningful when authenticated).</summary>
        public string TenantId { get; private init; } = string.Empty;

        /// <summary>The calling user id from the bearer, or <c>null</c> when it carried none.</summary>
        public string? UserId { get; private init; }

        public static ProxyGatewayAuthResult Fail() => new();

        public static ProxyGatewayAuthResult Success(string tenantId, string? userId) => new()
        {
            IsAuthenticated = true,
            TenantId = tenantId,
            UserId = userId,
        };
    }
}
