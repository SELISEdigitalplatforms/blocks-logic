using Blocks.Genesis;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace DomainService.OAuth
{
    public class GoogleLogInService : ISocialLogInService
    {
        private readonly ILogger<GoogleLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public GoogleLogInService(
            ILogger<GoogleLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService
        )
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _httpService = httpService;
        }

        public async Task<(string, bool)> GetProviderLogInUriAsync(GetSocialLogInEndPointRequest request)
        {
            var credential = await _authenticationRepository.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience);

            if (credential == null)
            {
                _logger.LogError($"Credential not found for provider {request.Provider} and audience {request.Audience}");
                return (string.Empty, true);
            }

            var socialLogInStateKey = Guid.NewGuid().ToString("n");
            var socialLogInStateInfo = new StateInfo
            {
                Audience = request.Audience,
                Provider = request.Provider,
                NextUrl = request.NextUrl,
            };

            await _cacheClient.AddStringValueAsync(socialLogInStateKey, JsonSerializer.Serialize(socialLogInStateInfo), 300);

            return (string.Format(credential.AuthorizationUrl, credential.Scope, socialLogInStateKey, WebUtility.UrlEncode(credential.RedirectUrl), credential.ClientId), request.SendAsResponse || credential.SendAsResponse);

        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var credential = await _authenticationRepository.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience);
            var postData = new Dictionary<string, string>
                {
                    { "code", stateInfo.Code },
                    { "client_id", credential.ClientId },
                    { "client_secret", credential.ClientSecret },
                    { "redirect_uri", credential.RedirectUrl },
                    { "grant_type", "authorization_code" }
                };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, credential.TokenUrl);

            if(!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError($"Error while getting access token: {error}");
                return new GoogleUserData();
            }

            var userAccessEndPoint = string.Format(credential.GetProfileUrl, response.AccessToken);

            (var externalUser, error) = await _httpService.Get<GoogleUserData>(userAccessEndPoint);

            if(!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError($"Error while getting user data: {error}");
                return new GoogleUserData();
            }

            externalUser.Permissions = credential?.InitialPermissions ?? [];
            externalUser.Roles = credential?.InitialRoles ?? [];
            externalUser.Platform = stateInfo.Provider;

            return externalUser;
        }
    }

}
