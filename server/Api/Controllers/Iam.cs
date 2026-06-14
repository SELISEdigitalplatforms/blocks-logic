using Blocks.Genesis;
using CloudConfiguration.DomainService.Authentication.RequestModel;
using CloudConfiguration.DomainService.IAM.RequestModel;
using CloudConfiguration.DomainService.IAM.ResponseModel;
using CloudConfiguration.DomainService.Shared.Services;
using Iam.DomainService.Accounts;
using Iam.DomainService.Activities;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.RequestModel;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("iam")]

    public class IamController : ControllerBase
    {
        private readonly IAccountService _accountService;
        private readonly IUserActivityService _userActivityService;
        private readonly IUserManagementQueryService _userManagementQueryService;
        private readonly IUserManagementMutationService _userManagementMutationService;
        private readonly IResourceMutationService _resourceMutationService;
        private readonly IResourceQueryService _resourceQueryService;
        private readonly IConfigurationService _configurationService;

        public IamController(IAccountService accountService,
                             IUserActivityService userActivityService,
                             IResourceMutationService resourceMutationService,
                             IResourceQueryService resourceQueryService,
                             IUserManagementQueryService userManagementQueryService,
                             IUserManagementMutationService userManagementMutationService, IConfigurationService configurationService)
        {
            _userActivityService = userActivityService;
            _resourceMutationService = resourceMutationService;
            _resourceQueryService = resourceQueryService;
            _userManagementQueryService = userManagementQueryService;
            _userManagementMutationService = userManagementMutationService;
            _accountService = accountService;
            _configurationService = configurationService;
        }

        #region Account

        [HttpPost("Activate")]
        public async Task<IActionResult> Activate([FromBody] ActivateUserRequest command)
        {

            var result = await _accountService.ActivateAccountAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("Recover")]
        public async Task<IActionResult> Recover([FromBody] RecoveryUserRequest command)
        {

            var result = await _accountService.RecoverAccountAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest command)
        {

            var result = await _accountService.ResetAccountPasswordAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("ChangePassword")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest command)
        {

            var result = await _accountService.ChangePasswordAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("ResendActivation")]
        public async Task<IActionResult> ResendActivation([FromBody] ResendActivationRequest command)
        {

            var result = await _accountService.ResendActivationAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("ValidateActivationCode")]
        public async Task<IActionResult> ValidateActivationCode([FromBody] ValidateActivationCodeRequest command)
        {

            var result = await _accountService.ValidateAccountActivationCodeAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        #endregion

        #region Activity

        [HttpGet("GetSessions")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetSessionsResponse> GetSessions([FromQuery] BaseActivityRequest query)
        {

            return await _userActivityService.GetSessionsAsync(query);
        }

        [HttpGet("GetHistories")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetHistorysResponse> GetHistories([FromQuery] BaseActivityRequest query)
        {

            return await _userActivityService.GetHistoriesAsync(query);
        }

        #endregion

        #region Resource

        [HttpPost("CreatePermission")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest command)
        {
            var result = await _resourceMutationService.CreatePermissionAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("UpdatePermission")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> UpdatePermission([FromBody] UpdatePermissionRequest command)
        {
            var result = await _resourceMutationService.UpdatePermissionAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("CreateRole")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest command)
        {
            var result = await _resourceMutationService.CreateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("UpdateRole")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> UpdateRole([FromBody] UpdateRoleRequest command)
        {
            var result = await _resourceMutationService.UpdateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("GetPermissions")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetPermissionsResponse> GetPermissions([FromBody] GetPermissionsRequest query)
        {
            return await _resourceQueryService.GetPermissionsAsync(query);
        }

        [HttpGet("GetPermissionsGroupBySeverity")]
        [Authorize]
        public async Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverity([FromQuery] GetPermissionGroupBySeverityRequest request)
        {

            return await _resourceQueryService.GetPermissionsGroupBySeverityAsync();
        }

        [HttpGet("GetPermission")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetPermissionResponse> GetPermission([FromQuery] GetPermissionRequest query)
        {

            return await _resourceQueryService.GetPermissionAsync(query.Id);
        }

        [HttpPost("GetRoles")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetRolesResponse> GetRoles([FromBody] GetRolesRequest query)
        {
            return await _resourceQueryService.GetRolesAsync(query);
        }

        [HttpGet("GetRole")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetRoleResponse> GetRole([FromQuery] GetRoleRequest query)
        {
            return await _resourceQueryService.GetRoleAsync(query.Id);
        }

        [HttpPost("SetRoles")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> SetRoles([FromBody] SetRolesRequest command)
        {
            var result = await _resourceMutationService.SetRolesAsync(command);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("GetResourceGroups")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync([FromQuery] GetResourceGroupRequest request)
        {
            return await _resourceQueryService.GetResourceGroupsAsync();
        }

        #endregion

        #region User

        [HttpPost("users/create")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest command)
        {
            var result = await _userManagementMutationService.CreateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("users/{id}")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> Update([FromRoute] string id, [FromBody] UpdateUserRequest command)
        {
            command.ItemId = id;
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("users/deactivate")]
        [Authorize]
        public async Task<IActionResult> Deactivate([FromBody] DeactivateUserRequest request)
        {
            var result = await _userManagementMutationService.DeactivateUserAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("UpdateAccount")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> UpdateAccount([FromBody] UpdateUserRequest command)
        {
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet("users")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetUsersResponse> GetUsers([FromQuery] GetUsersRequest query)
        {
            return await _userManagementQueryService.GetUsersAsync(query);
        }

        [HttpGet("users/{id}")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetUserResponse> GetUser([FromRoute] string id)
        {
            return await _userManagementQueryService.GetUserAsync(id);
        }

        [HttpGet("GetUserRoles")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetUserRolesResponse> GetUserRoles([FromQuery] GetUserRolesRequest query)
        {
            return await _userManagementQueryService.GetUserRolesAsync(query.Id);
        }

        [HttpGet("GetUserPermissions")]
        //   [ProtectedEndPoint]
        [Authorize]
        public async Task<GetUserPermissionsResponse> GetUserPermissions([FromQuery] GetUserPermissionsRequest query)
        {
            return await _userManagementQueryService.GetUserPermissionsAsync(query.Id);
        }

        [HttpPost("GetAccounts")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetAccountsResponse> GetAccounts([FromBody] GetAccountsRequest query)
        {
            return await _userManagementQueryService.GetAccountsAsync(query);
        }

        [HttpGet("GetAccount")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetAccountResponse> GetAccount()
        {
            return await _userManagementQueryService.GetAccountAsync();
        }

        [HttpGet("GetAccountRoles")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetAccountRolesResponse> GetAccountRoles()
        {
            return await _userManagementQueryService.GetAccountRolesAsync();
        }

        [HttpGet("GetAccountPermissions")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetAccountPermissionsResponse> GetAccountPermissions()
        {
            return await _userManagementQueryService.GetAccountPermissionsAsync();
        }

        [HttpPost("SaveRolesAndPermissions")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> SaveRolesAndPermissions(SaveRolesAndPermissionsRequest command)
        {
            var result = await _userManagementMutationService.SaveRolesAndPermissionsAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet("IsEmailAvaiable")]
        public async Task<IActionResult> IsEmailAvaiable([FromQuery] IsEmailAvaiableRequest query)
        {
            var result = await _userManagementQueryService.IsUserAvailableAsync(query);
            return Ok(new IsEmailAvaiableResponse
            {
                IsAvailable = result
            });
        }

        [Authorize]
        [HttpGet("GetUserTimelines")]
        public async Task<List<UserTimeline>> GetUserTimelinesAsync(GetUserTimeLineRequest request)
        {
            return await _userManagementQueryService.GetUserTimelinesAsync(request);
        }

        #endregion

        #region Organization

        [HttpPost("SaveOrganization")]
        [Authorize]
        public async Task<BaseResponse> SaveOrganization([FromBody]  SaveOrganizationRequest request)
        {

            return await _resourceMutationService.SaveOrganizationAsync(request);
        }

        [HttpGet("GetOrganizations")]
        [Authorize]
        public async Task<GetOrganizationsResponse> GetOrganizations([FromQuery] GetOrganizationsRequest request)
        {

            return await _resourceMutationService.GetOrganizationsAsync(request);
        }

        [HttpGet("GetOrganization")]
        [Authorize]
        public async Task<GetOrganizationResponse> GetOrganization([FromQuery]  GetOrganizationRequest request)
        {

            return await _resourceMutationService.GetOrganizationAsync(request);
        }

        [HttpPost("SaveOrganizationConfig")]
        [Authorize]
        public async Task<BaseResponse> SaveOrganizationConfig([FromBody] SaveOrganizationConfigRequest request)
        {

            return await _resourceMutationService.SaveganizationConfigAsync(request);
        }

        [HttpGet("GetOrganizationConfig")]
        [Authorize]
        public async Task<OrganizationConfig> GetOrganizationConfig([FromQuery] GetOrganizationConfigRequest request)
        {

            return await _resourceMutationService.GetOrganizationConfigAsync(request);
        }

        [HttpPost("SaveSignUpSetting")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<SaveSignUpSettingResponse> SaveSignUpSetting([FromBody] SaveSignUpSettingRequest request)
        {

            return await _accountService.SaveSingUpSettingAsync(request);
        }

        [HttpGet("GetSignUpSetting")]
        public async Task<SignUpSetting> GetSignUpSetting([FromQuery] GetSignUpSettingRequest request)
        {

            return await _accountService.GetSignUpSettingAsync(request);
        }

        #endregion
        #region Cloud configuration
        [HttpPost("Save")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<IActionResult> Save([FromBody] SaveIamConfigurationRequest request)
        {
            var result = await _configurationService.SaveIamConfigurationAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet("Get")]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetConfigurationResponse> Get([FromQuery] GetAuthenticationConfigurationRequest request)
        {
            return await _configurationService.GetIamConfigurationAsync();
        }
        #endregion
    }
}
