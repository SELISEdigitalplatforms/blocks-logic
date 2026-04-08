using Blocks.Genesis;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace DomainService.OAuth.SocialServices
{
    public class LinkedinLogInService : ISocialLogInService
    {
        private readonly ILogger<LinkedinLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public LinkedinLogInService(
            ILogger<LinkedinLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _httpService = httpService;
        }

        public async Task<(string, bool)> GetProviderLogInUriAsync(GetSocialLogInEndPointRequest request)
        {
            var credential = await _authenticationRepository
                .GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience);

            if (credential == null)
            {
                _logger.LogError($"Credential not found for provider {request.Provider} and audience {request.Audience}");
                return (string.Empty, true);
            }

            var stateKey = Guid.NewGuid().ToString("n");
            var stateInfo = new StateInfo
            {
                Audience = request.Audience,
                Provider = request.Provider,
                NextUrl = request.NextUrl,
            };

            await _cacheClient.AddStringValueAsync(stateKey, JsonSerializer.Serialize(stateInfo), 300);

            // Build LinkedIn login URL safely (scope must be URL encoded)
            var loginUri =
                $"{credential.AuthorizationUrl.Split('?')[0]}" +
                $"?response_type=code" +
                $"&client_id={credential.ClientId}" +
                $"&redirect_uri={WebUtility.UrlEncode(credential.RedirectUrl)}" +
                $"&scope={WebUtility.UrlEncode(credential.Scope).Replace("+", "%20").Replace(" ", "%20")}" +
                $"&state={stateKey}";
            _logger.LogError($"loginUri for provider {request.Provider} and loginUri {loginUri}");

            return (loginUri, request.SendAsResponse || credential.SendAsResponse);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var credential = await _authenticationRepository
                .GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience);

            var postData = new Dictionary<string, string>
            {
                { "code", stateInfo.Code },
                { "client_id", credential.ClientId },
                { "client_secret", credential.ClientSecret },
                { "redirect_uri", credential.RedirectUrl },
                { "grant_type", "authorization_code" }
            };

            var (tokenResponse, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(
                HttpMethod.Post,
                postData,
                credential.TokenUrl);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError($"Error while getting LinkedIn access token: {error}");
                return new LinkedinUserData();
            }

            var headers = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {tokenResponse.AccessToken}" },
                { "User-Agent", $"{credential.Audience}" } // LinkedIn requires this
            };

            var profileUrl = credential.GetProfileUrl + $"oauth2_access_token={tokenResponse.AccessToken}";

            (var userInfo, var profileError) = await _httpService.Get<LinkedinUserInfo>(
                profileUrl);

            var profile = new LinkedinUserData
            {
                ExternalProviderUserId = userInfo.Sub,
                FirstName = userInfo.Given_Name,
                LastName = userInfo.Family_Name,
                Email = userInfo.Email,
                DisplayName = userInfo.Name,
                ProfileImageUrl = userInfo.Picture
            };

            if (!string.IsNullOrWhiteSpace(profileError))
            {
                _logger.LogError($"Error while getting LinkedIn user profile: {profileError}");
                return new LinkedinUserData();
            }

            profile.Permissions = credential?.InitialPermissions ?? [];
            profile.Roles = credential?.InitialRoles ?? [];
            profile.Platform = stateInfo.Provider;

            return profile;
        }
    }
}
