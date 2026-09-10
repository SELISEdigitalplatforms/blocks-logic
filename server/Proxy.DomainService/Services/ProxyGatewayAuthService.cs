using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Validates the inbound data-plane call: the <c>X-Blocks-Key</c> header must name a known tenant
    /// (repo convention: <c>x-blocks-key == TenantId</c>) and the <c>Authorization: Bearer</c> token must be
    /// signed by that tenant's certificate. A bearer issued for a different tenant fails naturally because it
    /// is checked against the wrong tenant's public cert. Mirrors
    /// <c>WorkflowAuthService.ValidateTokenAsync</c>; no RBAC.
    /// </summary>
    public sealed class ProxyGatewayAuthService : IProxyGatewayAuthService
    {
        /// <summary>Tenant/project key header identifying the caller's tenant.</summary>
        public const string TenantKeyHeader = "X-Blocks-Key";

        private const string PublicCertCachePrefix = "tetocertpublic::";

        private readonly ITenants _tenants;
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<ProxyGatewayAuthService> _logger;

        public ProxyGatewayAuthService(
            ITenants tenants,
            ICacheClient cacheClient,
            ILogger<ProxyGatewayAuthService> logger)
        {
            _tenants = tenants;
            _cacheClient = cacheClient;
            _logger = logger;
        }

        public async Task<ProxyGatewayAuthResult> AuthenticateAsync(HttpRequest request)
        {
            var tenantKey = request.Headers[TenantKeyHeader].ToString();
            if (string.IsNullOrWhiteSpace(tenantKey))
            {
                _logger.LogWarning("Proxy gateway auth failed: missing {Header} header.", TenantKeyHeader);
                return ProxyGatewayAuthResult.Fail();
            }

            tenantKey = tenantKey.Trim();

            var tenant = _tenants.GetTenantByID(tenantKey);
            if (tenant is null)
            {
                _logger.LogWarning("Proxy gateway auth failed: {Header} names an unknown tenant {TenantId}.", TenantKeyHeader, tenantKey);
                return ProxyGatewayAuthResult.Fail();
            }

            var (token, _) = TokenHelper.GetToken(request, _tenants);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Proxy gateway auth failed: no bearer token. TenantId={TenantId}", tenant.TenantId);
                return ProxyGatewayAuthResult.Fail();
            }

            try
            {
                var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
                var cacheKey = $"{PublicCertCachePrefix}{tenant.TenantId}";
                var certificateData = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
                var jwtParameters = tenant.JwtTokenParameters;
                var publicCert = X509CertificateLoader.LoadPkcs12(certificateData, jwtParameters.PublicCertificatePassword);

                var validationParameters = new TokenValidationParameters
                {
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    IssuerSigningKey = new X509SecurityKey(publicCert),
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    SaveSigninToken = true,
                };

                var principal = handler.ValidateToken(token, validationParameters, out _);
                var userId = principal.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value;

                _logger.LogInformation(
                    "Proxy gateway auth ok. TenantId={TenantId}, UserId={UserId}",
                    tenant.TenantId, string.IsNullOrEmpty(userId) ? "(none)" : userId);

                return ProxyGatewayAuthResult.Success(tenant.TenantId, string.IsNullOrEmpty(userId) ? null : userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Proxy gateway auth failed: bearer validation threw. TenantId={TenantId}", tenant.TenantId);
                return ProxyGatewayAuthResult.Fail();
            }
        }
    }
}
