using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using HandlebarsDotNet.Runtime;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace DomainService.OAuth
{
    public class OAuthJwtAccessTokenManager : IOAuthJwtAccessTokenManager
    {
        private readonly IJwtAccessTokenProvider _jwtAccessTokenProvider;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IOtpServiceFactory _otpServiceFactory;
        private readonly IMfaConfigurationService _configurationService;
        private readonly IConfiguration _configuration;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;

        public OAuthJwtAccessTokenManager(
            IJwtAccessTokenProvider jwtAccessTokenProvider,
            IAuthenticationDomainService authenticationDomainService,
            IMfaConfigurationService configurationService,
            ICacheClient cacheClient,
            ITenants tenants,
            IOtpServiceFactory otpServiceFactory,
            IConfiguration configuration
        )
        {
            _jwtAccessTokenProvider = jwtAccessTokenProvider;
            _authenticationDomainService = authenticationDomainService;
            _configurationService = configurationService;
            _cacheClient = cacheClient;
            _tenants = tenants;
            _otpServiceFactory = otpServiceFactory;
            _configuration = configuration;
        }

        public async Task<TokenResponse> ManageTokenAsync(TokenRequest tokenRequest, AuthenticationConfiguration authenticationConfiguration, User user, StateInfo? stateInfo = null)
        {
            var bc = BlocksContext.GetContext();
            
            var tokenResponse = await ProcessCheckPoints(tokenRequest, user);

            if (tokenResponse != null && !string.IsNullOrWhiteSpace(tokenResponse.Error))
            {
                return tokenResponse;
            }

            var tenant = _tenants.GetTenantByID(bc?.TenantId ?? "");
            var jwtAccessToken = await _jwtAccessTokenProvider.GetJwtAccessToken(authenticationConfiguration, tenant, user, stateInfo, organizationId: tokenRequest.OrganizationId);
            jwtAccessToken.Audience = !string.IsNullOrWhiteSpace(stateInfo?.Audience) ? stateInfo.Audience : jwtAccessToken.Audience;
            jwtAccessToken.Issuer = tokenRequest.GrantType == GrantTypes.AuthCode ? _configuration["OpenIdConnect:IssuerUri"] ?? "Selise-Blocks": jwtAccessToken.Issuer;

            var accessToken = CreateJwtAccessToken(jwtAccessToken);
            var (refreshToken, refreshValidity) = await ManageRefreshTokenAsync(tokenRequest, jwtAccessToken, authenticationConfiguration, tenant, user);

            return new TokenResponse
            {
                AccessToken = accessToken,
                ExpiresIn = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                ExpiresUtc = jwtAccessToken.Expires,
                RefreshToken = refreshToken,
                RefreshExpiresUtc = refreshValidity,
                CookieDomain = tenant.CookieDomain,
                StatusCode = 200
            };
        }

        private async Task<TokenResponse> ProcessCheckPoints(TokenRequest tokenRequest, User user)
        {
            if (tokenRequest.GrantType != GrantTypes.MfaCode && tokenRequest.GrantType != GrantTypes.ClientCredential && await CheckIfMfaIsApplicable(user))
            {
                return await HandleMfaAuthentication(user);
            }

            return new TokenResponse(); //Will send proper response after 20.04.2025

            // return ProcessAccountLock(tenant, user); 
        }

        private async Task<TokenResponse> HandleMfaAuthentication(User user)
        {
            var otpService = _otpServiceFactory.GetOTPService(user.UserMfaType);
            var response = await otpService.GenerateAsync(new UserInfo { Email = user.Email, ItemId = user.ItemId, Language = user.Language ?? "en-US" });

            return new TokenResponse
            {
                MfaId = response.MfaId,
                UserMfa = user.UserMfaType,
                Error = "mfa_enabled",
                ErrorDescription = "Mfa code required",
                StatusCode = 200
            };
        }


        private async Task<bool> CheckIfMfaIsApplicable(User user)
        {
            var mfaConfiguration = await _configurationService.GetAsync();
            var mfaProviders = mfaConfiguration.UserMfaType ?? [];

            return user.MfaEnabled && mfaProviders.Contains(user.UserMfaType);
        }

        public static string CreateJwtAccessToken(JwtAccessToken jwtAccessToken, StateInfo? stateInfo = null)
        {
            

            var jwtToken = new JwtSecurityToken(
                jwtAccessToken.Issuer,
                jwtAccessToken.Audience,
                jwtAccessToken.Claims,
                jwtAccessToken.NotBefore,
                jwtAccessToken.Expires,
                jwtAccessToken.SigningCredentials);

            return new JwtSecurityTokenHandler().WriteToken(jwtToken);
        }

        public async Task<(string, DateTime)> ManageRefreshTokenAsync(TokenRequest tokenRequest, JwtAccessToken jwtAccessToken, AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user)
        {
            var visitorsIpAddresses = _authenticationDomainService.GetVisitorsIpAddresses(tokenRequest.Request.HttpContext);
            var refreshTokenId = Guid.NewGuid().ToString("N");

            var refreshTokenLifetime = tokenRequest.RememberMe && authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes > authenticationConfiguration.RefreshTokenValidForNumberMinutes
                ? authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes
                : Math.Max(authenticationConfiguration.RefreshTokenValidForNumberMinutes, 30);

            var refreshTokenExpireOn = jwtAccessToken.NotBefore.AddMinutes(refreshTokenLifetime);

            var refreshTokenCache = new RefreshTokenCache
            {
                RefreshToken = refreshTokenId,
                TenantId = tenant.TenantId,
                IssuedUtc = jwtAccessToken.NotBefore,
                ExpiresUtc = refreshTokenExpireOn,
                IpAddresses = string.Join(",", visitorsIpAddresses),
                UserId = user.ItemId
            };

            await _cacheClient.AddStringValueAsync(refreshTokenCache.RefreshToken, JsonSerializer.Serialize(refreshTokenCache), refreshTokenLifetime * 60);

            var addRefreshTokenCommand = new RefreshTokenEvent
            {
                RefreshToken = refreshTokenCache.RefreshToken,
                TenantId = refreshTokenCache.TenantId,
                IssuedUtc = refreshTokenCache.IssuedUtc,
                ExpiresUtc = refreshTokenCache.ExpiresUtc,
                IpAddresses = refreshTokenCache.IpAddresses,
                UserId = refreshTokenCache.UserId,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent)
            };
            //_authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, addRefreshTokenCommand),
            await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.IamQueue, addRefreshTokenCommand);
            

            return (refreshTokenId, refreshTokenExpireOn);
        }

        public TokenResponse ProcessAccountLock(AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user)
        {
            var lockKey = $"account-lock-{tenant.TenantId}-{user.ItemId}-{user.OrganizationIds?.FirstOrDefault() ?? "default"}";
            var isLocked = IsLocked(lockKey, authenticationConfiguration.GetNumberOfWrongAttemptsToLockTheAccount);

            if (!isLocked)
            {
                Lock(lockKey, authenticationConfiguration.AccountLockDurationInMinutes, authenticationConfiguration.GetNumberOfWrongAttemptsToLockTheAccount);
                return new TokenResponse();
            }

            return new TokenResponse { Error = OAuthError.AccountLocked, ErrorDescription = "Your account has been locked due to multiple failed login attempts" };
        }

        public void Lock(string key, int lockTimeInMinutes, int maxAttempts)
        {
            var lockCountValue = _cacheClient.GetStringValue(key);
            var lockCount = string.IsNullOrWhiteSpace(lockCountValue) ? 0 : int.Parse(lockCountValue);

            if (lockCount >= maxAttempts)
            {
                return;
            }

            _cacheClient.AddStringValue(key, (lockCount + 1).ToString(), lockTimeInMinutes * 60);
        }

        public bool IsLocked(string key, int maxAttempts)
        {
            var lockCountValue = _cacheClient.GetStringValue(key);

            return !string.IsNullOrWhiteSpace(lockCountValue) && int.Parse(lockCountValue) >= maxAttempts;
        }
    }
}
