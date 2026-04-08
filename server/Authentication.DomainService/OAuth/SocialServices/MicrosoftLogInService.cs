using Blocks.Genesis;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;

namespace DomainService.OAuth
{
    public class MicrosoftLogInService : ISocialLogInService
    {
        private readonly ILogger<MicrosoftLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public MicrosoftLogInService(
            ILogger<MicrosoftLogInService> logger,
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
                    { "grant_type", "authorization_code" },
                    {"scope", "openid profile email" }
                };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, credential.TokenUrl);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError($"Error while getting access token: {error}");
                return new MicrosoftUserData();
            }

            var queryPram = "$select=displayName,mail,department,employeeId,givenName,userPrincipalName,surname,officeLocation,preferredLanguage,mobilePhone,id";
            var profileURL = $"{credential.GetProfileUrl}?{queryPram}";

            (var externalUser, error) = await _httpService.Get<MicrosoftUserData>(profileURL, new Dictionary<string, string> {
                { "Authorization", $"bearer {response.AccessToken}"  }
            });

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError($"Error while getting user data: {error}");
                return new MicrosoftUserData();
            }

            externalUser.Roles = ExtractRolesFromJwt(response.IdToken);
            Console.WriteLine($"IntraId Roles: {string.Join(", ", externalUser.Roles)}");
            if (externalUser.Roles.Count > 0)
                externalUser.Roles.AddRange(credential.InitialRoles);
            else
                externalUser.Roles = credential.InitialRoles;

            externalUser.Permissions = credential.InitialPermissions;
            externalUser.Platform = stateInfo.Provider;

            return externalUser;
        }

        //public static List<string> ExtractRolesFromJwt(string jwt)
        //{
        //    var handler = new JwtSecurityTokenHandler();
        //    var token = handler.ReadJwtToken(jwt); // NOTE: this does NOT validate signature

        //    // Look for "roles" claim (your token uses "roles")
        //    var rolesClaim = token.Claims.FirstOrDefault(c => c.Type == "roles")?.Value;


        //    if (string.IsNullOrWhiteSpace(rolesClaim))
        //        return new List<string>();

        //    // If roles is a JSON array, parse it; otherwise treat as single role string
        //    rolesClaim = rolesClaim.Trim();

        //    if (rolesClaim.StartsWith("["))
        //    {
        //        return JsonSerializer.Deserialize<List<string>>(rolesClaim) ?? new List<string>();
        //    }

        //    return new List<string> { rolesClaim };
        //}

        private static List<string> ExtractRolesFromJwt(string jwt)
        {
            var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);

            // get ALL "roles" claims (not just the first)
            var values = token.Claims
                .Where(c => c.Type == "roles")
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            var roles = new List<string>();

            foreach (var v in values)
            {
                var s = v.Trim();

                // if a single "roles" claim contains JSON array: ["admin","user"]
                if (s.StartsWith("["))
                {
                    var arr = JsonSerializer.Deserialize<List<string>>(s);
                    if (arr != null) roles.AddRange(arr);
                }
                else
                {
                    // otherwise each claim value is a single role: "user"
                    roles.Add(s);
                }
            }

            return roles;
        }
    }
}
