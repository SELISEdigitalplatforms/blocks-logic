using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;


namespace DomainService.Workflow.Services
{
    public class WorkflowAuthService : IWorkflowAuthService
    {
        private readonly ITenants _tenants;
        private readonly ICacheClient _cacheClient;
        private const string PublicCertCachePrefix = "tetocertpublic::";
        public WorkflowAuthService(ITenants tenants, ICacheClient cacheClient)
        {
            _tenants = tenants;
            _cacheClient = cacheClient;
        }

        public static string GetAudience(Tenant? tenant)
        {
            var configuredAudience = tenant?.JwtTokenParameters?.Audiences?
                .FirstOrDefault(audience => !string.IsNullOrWhiteSpace(audience));

            if (!string.IsNullOrWhiteSpace(configuredAudience))
            {
                return configuredAudience.Trim();
            }

            return "api://blocks-protected-api";
        }
        public async Task<bool> IsAuthenticated(HttpRequest request, string tenantId)
        {
            var (token, _) = TokenHelper.GetToken(request, _tenants);
            var tenant = _tenants.GetTenantByID(tenantId);
            if (tenant == null)
            {
                return false;
            }
            if (string.IsNullOrEmpty(token)) return false;

            var tokenHandler = new JwtSecurityTokenHandler();
            string cacheKey = $"{PublicCertCachePrefix}{tenant.TenantId}";
            var certificateData = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
            var validationParams = tenant.JwtTokenParameters;
            var publicCert = X509CertificateLoader.LoadPkcs12(certificateData, validationParams.PublicCertificatePassword);
            var tokenValidationParameters = new TokenValidationParameters { ValidateLifetime = true, ClockSkew = TimeSpan.Zero, IssuerSigningKey = new X509SecurityKey(publicCert), ValidateIssuerSigningKey = true, ValidateIssuer = false, ValidateAudience = false, SaveSigninToken = true };
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out _);
            if (principal == null) return false;
            return true;
        }
    }
}