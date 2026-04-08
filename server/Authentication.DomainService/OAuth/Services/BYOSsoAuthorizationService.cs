using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DomainService.OAuth.Services
{
    public class BYOSsoAuthorizationService : ITokenService
    {
        private readonly ILogger<BYOSsoAuthorizationService> _logger;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly ICacheClient _cacheClient;
        private readonly ISocialLogInServiceProvider _socialLogInServiceProvider;
        private readonly IUserManagementMutationService _userManagementMutationService;
        private readonly IUserRepository _userRepository;

        public BYOSsoAuthorizationService(
            ILogger<BYOSsoAuthorizationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationRepository oAuthRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
           IUserManagementMutationService userManagementMutationService,
           IUserRepository userRepository)
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _oAuthRepository = oAuthRepository;
            _cacheClient = cacheClient;
            _socialLogInServiceProvider = socialLogInServiceProvider;
            _userManagementMutationService = userManagementMutationService;
            _userRepository = userRepository;
        }
        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            _logger.LogInformation("Social Authentication start");

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                _logger.LogError("Code is required");
                return new TokenResponse { Error = "code_require", ErrorDescription = "code_require", StatusCode = 400 };
            }

            if (string.IsNullOrWhiteSpace(request.State))
            {
                return new TokenResponse { Error = "state_require", ErrorDescription = "state_require", StatusCode = 400 };
            }

            var stateCacheData = await _cacheClient.GetStringValueAsync(request.State);

            if (string.IsNullOrWhiteSpace(stateCacheData))
            {
                _logger.LogError("State data not found");
                return new TokenResponse { Error = "state_data_not_found", ErrorDescription = "state_data_not_found", StatusCode = 400 };
            }

            var stateInfo = JsonSerializer.Deserialize<StateInfo>(stateCacheData);
            if (stateInfo == null)
            {
                _logger.LogError("State data is invalid");
                return new TokenResponse { Error = "state_data_invalid", ErrorDescription = "state_data_invalid", StatusCode = 400 };
            }

            stateInfo.Code = request.Code;

            var externalUser = await _socialLogInServiceProvider.HandleSocialLogin(stateInfo);
            await _cacheClient.RemoveKeyAsync(request.State); 

            if (string.IsNullOrWhiteSpace(externalUser.Email))
            {
                return new TokenResponse { Error = "External provider did not provided any email", ErrorDescription = "External provider did not provided any email", StatusCode = 401 };
            }

            if (string.IsNullOrWhiteSpace(externalUser.ExternalProviderUserId))
            {
                return new TokenResponse { Error = "External provider did not provided any user id", ErrorDescription = "External provider did not provided any user id", StatusCode = 401 };
            }

            user = await GetUser(stateInfo, externalUser);

            if (user == null)
            {
                return new TokenResponse { Error = "Failed to create user", ErrorDescription = "Failed to create user", StatusCode = 401 };
            }

            if (!user.Active || !user.IsVarified)
            {
                return new TokenResponse { Error = "There is a user with external user id but is not active", ErrorDescription = "There is a user with external user id but is not active", StatusCode = 401 };
            }
            return await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);

        }

        public async Task<User?> GetUser(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var user = await _oAuthRepository.GetUserByEmailAsync(externalUser.Email);

            if (user == null)
                return await CreateUser(stateInfo, externalUser);

            user.DepartMent = externalUser.Department;
            user.EmployeeId = externalUser.EmployeeId;
            user.Memberships = [new OrganizationMembership { Roles = externalUser.Roles, OrganizationId = "default" }];
            await _userRepository.UpdateUserAsync(user);

            return user;

        }

        public async Task<User?> CreateUser(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var blocksContext = BlocksContext.GetContext();

            var userPayload = new CreateUserViaSsoRequest
            {
                Email = externalUser.Email,
                ExternalUserId = externalUser.ExternalProviderUserId,
                FirstName = externalUser.FirstName,
                LastName = externalUser.LastName,
                PhoneNumber = externalUser.PhoneNumber,
                IsVarified = true,
                Active = true,
                MailPurpose = "AccountActivated",
                SendWelcomeMail = true,
                Platform = stateInfo.Provider,
                ProfileImageUrl = externalUser.ProfileImageUrl,
                Memberships = [new OrganizationMembership { Roles = externalUser.Roles, OrganizationId = "default" }],
                Permissions = externalUser.Permissions ?? [],
                ProjectKey = blocksContext.TenantId,
            };

            var result = await _userManagementMutationService.CreateUserViaSsoAsync(userPayload);

            if (result == null || !result.IsSuccess)
            {
                _logger.LogError("Failed to create user via SSO -- {res}", JsonSerializer.Serialize(result));
                return null;
            }

            return await _oAuthRepository.GetUserByIdAsync(result.ItemId);
        }
    }
}