using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using DomainService.Shared.Entities;
using FluentAssertions;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Identifier
{
    public class ProjectManagementServiceTests : IDisposable
    {
        private readonly Mock<IProjectRepository> _projectRepository = new();

        public ProjectManagementServiceTests()
        {
            TestBlocksContext.Set();
        }

        public void Dispose() => TestBlocksContext.Clear();

        private ProjectManagementService CreateService() => new(_projectRepository.Object);

        private static Tenant Project(string tenantId = "DTENANT-1") => new()
        {
            ItemId = "project-item-1",
            TenantId = tenantId,
            TenantGroupId = "group-1",
            Name = "Demo",
            Environment = "dev",
            DbConnectionString = "mongodb://localhost",
            Applications = [new Applications { Domain = "https://demo.test", CookieDomain = "demo.test", IsDomainVerified = true }],
            JwtTokenParameters = new JwtTokenParameters
            {
                PrivateCertificatePassword = "private-pw",
                PublicCertificatePassword = "public-pw",
                IssueDate = DateTime.UtcNow
            }
        };

        #region GetAllAsync

        [Fact]
        public async Task GetAllAsync_DelegatesToRepository()
        {
            var expected = new List<GroupedProjectsDto> { new() { TenantGroupId = "group-1" } };
            _projectRepository.Setup(r => r.GetAllByLastModifiedDateAsync(It.IsAny<GetProjectsRequest>())).ReturnsAsync(expected);

            var result = await CreateService().GetAllAsync(new GetProjectsRequest());

            result.Should().BeSameAs(expected);
        }

        #endregion

        #region GetAsync

        [Fact]
        public async Task GetAsync_ProjectNotFound_ReturnsProjectNotExistError()
        {
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync((Tenant)null!);

            var result = await CreateService().GetAsync();

            result.Data.Should().BeNull();
            result.Errors.Should().ContainKey("project_not_exist");
        }

        [Fact]
        public async Task GetAsync_WithBlocksGuid_BuildsTenantSlug()
        {
            var project = Project();
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync(project);
            _projectRepository.Setup(r => r.GetBlocksGuidAsync("group-1"))
                .ReturnsAsync(new BlocksGuid { EncodedValue = "xyzab" });

            var result = await CreateService().GetAsync();

            result.Errors.Should().BeNull();
            result.Data!.TenantSlug.Should().Be("dxyzab");
            result.Data.Name.Should().Be("Demo");
            result.Data.IsDomainVerified.Should().BeTrue();
        }

        [Fact]
        public async Task GetAsync_WithoutBlocksGuid_LeavesTenantSlugEmpty()
        {
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync(Project());
            _projectRepository.Setup(r => r.GetBlocksGuidAsync("group-1")).ReturnsAsync((BlocksGuid)null!);

            var result = await CreateService().GetAsync();

            result.Data!.TenantSlug.Should().BeEmpty();
        }

        #endregion
    }
}
