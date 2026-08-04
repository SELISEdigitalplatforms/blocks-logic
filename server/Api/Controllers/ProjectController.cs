using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class ProjectController : ControllerBase
    {
        private readonly IProjectManagementService _projectManagementService;
        private readonly IValidator<CreateProjectRequest> _createProjectValidator;
        private readonly IValidator<UpdateProjectRequest> _updateProjectValidator;


        public ProjectController(IProjectManagementService projectManagementService,
                                 IValidator<CreateProjectRequest> createProjectValidator,
                                 IValidator<UpdateProjectRequest> updateProjectValidator
                                 )
        {
            _projectManagementService = projectManagementService;
            _createProjectValidator = createProjectValidator;
            _updateProjectValidator = updateProjectValidator;

        }

        //[HttpPost]
        //[Authorize]
        //public async Task<CreateProjectResponse> Create([FromBody] CreateProjectRequest request)
        //{
        //    var validationResult = await _createProjectValidator.ValidateAsync(request);

        //    if (!validationResult.IsValid)
        //    {
        //        return new CreateProjectResponse { Errors = validationResult.Errors.ToDictionary(e => string.IsNullOrWhiteSpace(e.PropertyName) ? "validation_error" : e.PropertyName, e => e.ErrorMessage), IsSuccess = false };
        //    }

        //    return await _projectManagementService.SaveProjectAsync(request);
        //}


        [HttpGet]
        [Authorize]
        public async Task<List<GroupedProjectsDto>> Gets([FromQuery] GetProjectsRequest request)
        {
            return await _projectManagementService.GetAllAsync(request);
        }

        //[HttpPost]
        //[Authorize]
        //public async Task<RestoreProjectResponse> Restore([FromBody] RestoreProjectRequest restoreProjectRequest)
        //{
        //    return await _projectManagementService.RestoreProjectAsync(restoreProjectRequest);
        //}

        [HttpGet]
        [Authorize]
        public async Task<GetProjectResponse> Get()
        {
            return await _projectManagementService.GetAsync();
        }

        //[HttpPost]
        //[Authorize]
        //public async Task<BaseResponse> UpdateProject([FromBody] UpdateProjectRequest request)
        //{
        //    var validationResult = await _updateProjectValidator.ValidateAsync(request);

        //    if (!validationResult.IsValid)
        //    {
        //        return new BaseResponse { IsSuccess = false, Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
        //    }

        //    return await _projectManagementService.UpdateProjectAsync(request);
        //}

        //[Authorize]
        //[HttpPost]
        //public async Task<BaseResponse> UpdateTenantGroup([FromBody] UpdateTenantGroupRequest request)
        //{
        //    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TenantGroupId))
        //    {
        //        return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "property_missing", "TenantGroupId or ProjectNane should not be empty" } } };
        //    }

        //    return await _projectManagementService.UpdateTenantGroupAsync(request);
        //}

        //[HttpPost]
        //[Authorize]
        //public async Task<BaseResponse> Disable([FromBody] DisableProjectRequest request)
        //{
        //    if (string.IsNullOrWhiteSpace(BlocksContext.GetContext().TenantId))
        //    {
        //        return new AuthConfigResponse { Errors = new Dictionary<string, string> { { "missing_projectKey", "ProjectKey is required" } } };
        //    }

        //    return await _projectManagementService.DisableProjectAsync(BlocksContext.GetContext().TenantId);
        //}

        [HttpGet]
        [Authorize]
        public async Task<GetAssetResponse> GetAsset([FromQuery] GetAssetRequest request)
        {
            return await _projectManagementService.GetAssetAsync(request);
        }

        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> AddAsset([FromBody] AddAssetRequest asset)
        {
            if (string.IsNullOrWhiteSpace(asset.TenantGroupId) || asset.Resource == null)
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "invalid_asset", "Asset or GroupId cannot be null or empty" } } };
            }

            await _projectManagementService.AddAssetAsync(asset);
            return new BaseResponse { IsSuccess = true };
        }

        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> UpdateTokenValidationParameters([FromBody] UpdateTokenValidationParametersRequest request)
        {
            return await _projectManagementService.UpdateTokenValidationParametersAsync(request);
        }

        //[HttpGet]
        //[Authorize]
        //public async Task<IActionResult> GetTokenValidationParameters([FromQuery] GetTokenValidationParametersRequest request)
        //{
        //    return await _projectManagementService.GetProjectTokenValidationParametersAsync(BlocksContext.GetContext().TenantId);
        //}

        //[HttpPost]
        //[Authorize]
        //public async Task<SaveThirdPartyJWTClaimsResponse> SaveThirdPartyJWTClaims([FromBody] SaveThirdPartyJWTClaimsRequest request)
        //{

        //    return await _projectManagementService.SaveThirdPartyJWTClaimsAsync(request);
        //}

        //[HttpGet]
        //[Authorize]
        //public async Task<ThirdPartyJWTClaims?> GetThirdPartyJWTClaims([FromQuery] GetThirdPartyJWTClaimsRequest request)
        //{
        //    return await _projectManagementService.GetThirdPartyJWTClaimsAsync(request);
        //}
    }
}
