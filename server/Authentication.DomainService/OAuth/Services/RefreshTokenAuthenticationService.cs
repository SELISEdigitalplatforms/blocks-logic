using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;

namespace DomainService.OAuth
{
    public class RefreshTokenAuthenticationService : ITokenService
    {
        private readonly ILogger<RefreshTokenAuthenticationService> _logger;
        private readonly IJwtAccessTokenProvider _jwtAccessTokenProvider;
        private readonly ITenants _tenants;
        public RefreshTokenAuthenticationService(
            ILogger<RefreshTokenAuthenticationService> logger,
            IJwtAccessTokenProvider jwtAccessTokenProvider,
            ITenants tenants
        )
        {
            _logger = logger;
            _jwtAccessTokenProvider = jwtAccessTokenProvider;
            _tenants = tenants;
        }
        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User user)
        {
            _logger.LogInformation("Authenticate start for RefreshToken");
            var bc = BlocksContext.GetContext();
            var tenant = _tenants.GetTenantByID(bc?.TenantId);
            var jwtAccessToken = await _jwtAccessTokenProvider.GetJwtAccessToken(authenticationConfiguration, tenant, user, organizationId: request.OrganizationId);
            var jwtToken = new JwtSecurityToken(
                jwtAccessToken.Issuer,
                jwtAccessToken.Audience,
                jwtAccessToken.Claims,
                jwtAccessToken.NotBefore,
                jwtAccessToken.Expires,
                jwtAccessToken.SigningCredentials);

            var accessToken = new JwtSecurityTokenHandler().WriteToken(jwtToken);

            return new TokenResponse
            {
                AccessToken = accessToken,
                ExpiresIn = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                ExpiresUtc = jwtAccessToken.Expires,
                CookieDomain = tenant.CookieDomain,
            };
        }
    }
}
