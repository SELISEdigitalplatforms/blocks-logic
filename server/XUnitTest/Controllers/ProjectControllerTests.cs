using Api.Controllers;
using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Controllers
{
    /// <summary>
    /// Unit tests for <see cref="ProjectController"/>. The controller is thin, so what is worth
    /// pinning is where it refuses: the two validated endpoints must not reach the service when
    /// validation fails, the hand rolled guards must produce the same shape of error as the
    /// validated ones, and the tenant-scoped endpoints must take the tenant from the ambient
    /// context rather than from the request body.
    /// </summary>
    public class ProjectControllerTests : IDisposable
    {
        private readonly Mock<IProjectManagementService> _service = new();
        private readonly Mock<IValidator<CreateProjectRequest>> _createValidator = new();
        private readonly Mock<IValidator<UpdateProjectRequest>> _updateValidator = new();
        private readonly ProjectController _controller;

        public ProjectControllerTests()
        {
            BlocksContext.IsTestMode = true;
            SetTenant("tenant-1");

            _createValidator
                .Setup(v => v.ValidateAsync(It.IsAny<CreateProjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _updateValidator
                .Setup(v => v.ValidateAsync(It.IsAny<UpdateProjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            _controller = new ProjectController(
                _service.Object, _createValidator.Object, _updateValidator.Object);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetTenant(string tenantId) =>
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId, null, "user-1", true, null, null,
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", tenantId));

        private static ValidationResult Invalid(string property, string message) =>
            new(new[] { new ValidationFailure(property, message) });

        // ---- Create ----

        [Fact]
        public async Task Create_PassesAValidRequestToTheService()
        {
            var request = new CreateProjectRequest();
            _service.Setup(s => s.SaveProjectAsync(request))
                    .ReturnsAsync(new CreateProjectResponse { IsSuccess = true, TenantGroupId = "tg-1" });

            var result = await _controller.Create(request);

            result.IsSuccess.Should().BeTrue();
            result.TenantGroupId.Should().Be("tg-1");
        }

        [Fact]
        public async Task Create_DoesNotReachTheServiceWhenValidationFails()
        {
            _createValidator
                .Setup(v => v.ValidateAsync(It.IsAny<CreateProjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Invalid("Name", "Name is required"));

            var result = await _controller.Create(new CreateProjectRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Name").WhoseValue.Should().Be("Name is required");
            _service.Verify(s => s.SaveProjectAsync(It.IsAny<CreateProjectRequest>()), Times.Never);
        }

        [Fact]
        public async Task Create_GivesAnUnnamedValidationFailureAFallbackKey()
        {
            // A rule with no property name would otherwise produce an empty dictionary key,
            // which the client cannot map back to a field or display.
            _createValidator
                .Setup(v => v.ValidateAsync(It.IsAny<CreateProjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Invalid("", "The project is not allowed in this region"));

            var result = await _controller.Create(new CreateProjectRequest());

            result.Errors.Should().ContainKey("validation_error");
        }

        // ---- UpdateProject ----

        [Fact]
        public async Task UpdateProject_PassesAValidRequestToTheService()
        {
            var request = new UpdateProjectRequest();
            _service.Setup(s => s.UpdateProjectAsync(request))
                    .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await _controller.UpdateProject(request);

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateProject_DoesNotReachTheServiceWhenValidationFails()
        {
            _updateValidator
                .Setup(v => v.ValidateAsync(It.IsAny<UpdateProjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Invalid("Name", "Name is required"));

            var result = await _controller.UpdateProject(new UpdateProjectRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Name");
            _service.Verify(s => s.UpdateProjectAsync(It.IsAny<UpdateProjectRequest>()), Times.Never);
        }

        // ---- UpdateTenantGroup, guarded by hand rather than by a validator ----

        [Theory]
        [InlineData("", "a name")]
        [InlineData("   ", "a name")]
        [InlineData("tg-1", "")]
        [InlineData("tg-1", "   ")]
        public async Task UpdateTenantGroup_RefusesAMissingIdOrName(string tenantGroupId, string name)
        {
            var result = await _controller.UpdateTenantGroup(
                new UpdateTenantGroupRequest { TenantGroupId = tenantGroupId, Name = name });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("property_missing");
            _service.Verify(s => s.UpdateTenantGroupAsync(It.IsAny<UpdateTenantGroupRequest>()), Times.Never);
        }

        [Fact]
        public async Task UpdateTenantGroup_PassesACompleteRequestThrough()
        {
            var request = new UpdateTenantGroupRequest { TenantGroupId = "tg-1", Name = "Renamed" };
            _service.Setup(s => s.UpdateTenantGroupAsync(request))
                    .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await _controller.UpdateTenantGroup(request);

            result.IsSuccess.Should().BeTrue();
        }

        // ---- AddAsset ----

        [Fact]
        public async Task AddAsset_RefusesAMissingGroupId()
        {
            var result = await _controller.AddAsset(
                new AddAssetRequest { TenantGroupId = "", Resource = new Resource() });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("invalid_asset");
            _service.Verify(s => s.AddAssetAsync(It.IsAny<AddAssetRequest>()), Times.Never);
        }

        [Fact]
        public async Task AddAsset_RefusesAMissingResource()
        {
            var result = await _controller.AddAsset(
                new AddAssetRequest { TenantGroupId = "tg-1", Resource = null! });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("invalid_asset");
            _service.Verify(s => s.AddAssetAsync(It.IsAny<AddAssetRequest>()), Times.Never);
        }

        [Fact]
        public async Task AddAsset_ReportsSuccessAfterStoringTheAsset()
        {
            // The service returns void here, so the controller synthesises the success
            // response and the call has to be verified rather than read from the result.
            var request = new AddAssetRequest { TenantGroupId = "tg-1", Resource = new Resource() };

            var result = await _controller.AddAsset(request);

            result.IsSuccess.Should().BeTrue();
            _service.Verify(s => s.AddAssetAsync(request), Times.Once);
        }

        // ---- endpoints scoped by the ambient tenant ----

        [Fact]
        public async Task Disable_DisablesTheCallingTenantRatherThanOneNamedInTheBody()
        {
            // DisableProjectRequest carries no fields at all, which is deliberate: the tenant
            // comes from the token so one project cannot disable another.
            _service.Setup(s => s.DisableProjectAsync("tenant-1"))
                    .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await _controller.Disable(new DisableProjectRequest());

            result.IsSuccess.Should().BeTrue();
            _service.Verify(s => s.DisableProjectAsync("tenant-1"), Times.Once);
        }

        [Fact]
        public async Task Disable_RefusesWhenTheContextCarriesNoTenant()
        {
            SetTenant("");

            var result = await _controller.Disable(new DisableProjectRequest());

            result.Errors.Should().ContainKey("missing_projectKey");
            _service.Verify(s => s.DisableProjectAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetTokenValidationParameters_ReadsTheCallingTenant()
        {
            _service.Setup(s => s.GetProjectTokenValidationParametersAsync("tenant-1"))
                    .ReturnsAsync(new OkResult());

            var result = await _controller.GetTokenValidationParameters(
                new GetTokenValidationParametersRequest());

            result.Should().BeOfType<OkResult>();
            _service.Verify(s => s.GetProjectTokenValidationParametersAsync("tenant-1"), Times.Once);
        }

        // ---- straight pass-through endpoints ----

        [Fact]
        public async Task Gets_ReturnsTheGroupedProjects()
        {
            var request = new GetProjectsRequest();
            _service.Setup(s => s.GetAllAsync(request))
                    .ReturnsAsync(new List<GroupedProjectsDto> { new(), new() });

            var result = await _controller.Gets(request);

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task Get_ReturnsTheCurrentProject()
        {
            var response = new GetProjectResponse();
            _service.Setup(s => s.GetAsync()).ReturnsAsync(response);

            var result = await _controller.Get();

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task Restore_PassesTheRequestThrough()
        {
            var request = new RestoreProjectRequest();
            _service.Setup(s => s.RestoreProjectAsync(request))
                    .ReturnsAsync(new RestoreProjectResponse { IsSuccess = true });

            var result = await _controller.Restore(request);

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetAsset_PassesTheRequestThrough()
        {
            var request = new GetAssetRequest();
            _service.Setup(s => s.GetAssetAsync(request))
                    .ReturnsAsync(new GetAssetResponse { IsSuccess = true });

            var result = await _controller.GetAsset(request);

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateTokenValidationParameters_PassesTheRequestThrough()
        {
            var request = new UpdateTokenValidationParametersRequest();
            _service.Setup(s => s.UpdateTokenValidationParametersAsync(request))
                    .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await _controller.UpdateTokenValidationParameters(request);

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task SaveThirdPartyJWTClaims_PassesTheRequestThrough()
        {
            var request = new SaveThirdPartyJWTClaimsRequest();
            _service.Setup(s => s.SaveThirdPartyJWTClaimsAsync(request))
                    .ReturnsAsync(new SaveThirdPartyJWTClaimsResponse { IsSuccess = true });

            var result = await _controller.SaveThirdPartyJWTClaims(request);

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetThirdPartyJWTClaims_ReturnsNullWhenNoneAreConfigured()
        {
            var request = new GetThirdPartyJWTClaimsRequest();
            _service.Setup(s => s.GetThirdPartyJWTClaimsAsync(request))
                    .ReturnsAsync((ThirdPartyJWTClaims)null!);

            var result = await _controller.GetThirdPartyJWTClaims(request);

            result.Should().BeNull();
        }
    }
}
