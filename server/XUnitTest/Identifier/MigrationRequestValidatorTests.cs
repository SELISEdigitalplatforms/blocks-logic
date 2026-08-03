using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Migration;
using DomainService.Projects;
using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;

namespace XUnitTest.Identifier
{
    public class MigrationRequestValidatorTests
    {
        private readonly Mock<IProjectRepository> _repo = new();
        private readonly MigrationRequestValidator _validator;

        public MigrationRequestValidatorTests()
        {
            _validator = new MigrationRequestValidator(_repo.Object);
        }

        private static MigrationRequest Request(
            string tenantGroup = "group-1",
            string projectKey = "pk-1",
            string targetKey = "pk-2") => new()
        {
            TenantGroupId = tenantGroup,
            ProjectKey = projectKey,
            TargetedProjectKey = targetKey,
            Services = new List<ServiceDetails>()
        };

        private void SetupProjects(params string[] tenantIds)
        {
            var grouped = new List<GroupedProjectsDto>
            {
                new()
                {
                    TenantGroupId = "group-1",
                    Projects = tenantIds.Select(t => new Project { TenantId = t }).ToList()
                }
            };
            _repo.Setup(r => r.GetAllByLastModifiedDateAsync(It.IsAny<GetProjectsRequest>()))
                .ReturnsAsync(grouped);
        }

        [Fact]
        public async Task BothKeysExist_Passes()
        {
            SetupProjects("pk-1", "pk-2");
            var result = await _validator.TestValidateAsync(Request());
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task EmptyTenantGroupId_Fails()
        {
            var result = await _validator.TestValidateAsync(Request(tenantGroup: ""));
            result.ShouldHaveValidationErrorFor(x => x.TenantGroupId);
        }

        [Fact]
        public async Task EmptyProjectKey_Fails()
        {
            var result = await _validator.TestValidateAsync(Request(projectKey: ""));
            result.ShouldHaveValidationErrorFor(x => x.ProjectKey);
        }

        [Fact]
        public async Task EmptyTargetedProjectKey_Fails()
        {
            var result = await _validator.TestValidateAsync(Request(targetKey: ""));
            result.ShouldHaveValidationErrorFor(x => x.TargetedProjectKey);
        }

        [Fact]
        public async Task MissingOneKey_Fails()
        {
            SetupProjects("pk-1");
            var result = await _validator.TestValidateAsync(Request());
            result.ShouldHaveValidationErrorFor(x => x);
        }

        [Fact]
        public async Task NoProjects_Fails()
        {
            _repo.Setup(r => r.GetAllByLastModifiedDateAsync(It.IsAny<GetProjectsRequest>()))
                .ReturnsAsync(new List<GroupedProjectsDto>());
            var result = await _validator.TestValidateAsync(Request());
            result.ShouldHaveValidationErrorFor(x => x);
        }

        [Fact]
        public async Task RepositoryThrows_TreatedAsInvalid()
        {
            _repo.Setup(r => r.GetAllByLastModifiedDateAsync(It.IsAny<GetProjectsRequest>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var result = await _validator.TestValidateAsync(Request());
            result.ShouldHaveValidationErrorFor(x => x);
        }
    }
}
