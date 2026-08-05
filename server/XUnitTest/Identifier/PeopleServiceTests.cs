using DomainService.Dtos;
using DomainService.People;
using DomainService.Projects;
using FluentAssertions;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Identifier
{
    public class PeopleServiceTests : IDisposable
    {
        private readonly Mock<IPeopleRepository> _peopleRepository = new();
        private readonly Mock<IProjectRepository> _projectRepository = new();

        public PeopleServiceTests()
        {
            TestBlocksContext.Set();
        }

        public void Dispose() => TestBlocksContext.Clear();

        private PeopleService CreateService() => new(_peopleRepository.Object, _projectRepository.Object);

        #region GetPeoplesAsync

        [Fact]
        public async Task GetPeoplesAsync_NullRequest_ReturnsEmptyGroupIdError()
        {
            var result = await CreateService().GetPeoplesAsync(null!);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("empty_group_id");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetPeoplesAsync_BlankGroupId_ReturnsEmptyGroupIdError(string groupId)
        {
            var result = await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = groupId });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("empty_group_id");
        }

        [Fact]
        public async Task GetPeoplesAsync_NoSharedProjects_ReturnsNoProjectsError()
        {
            _projectRepository.Setup(r => r.GetProjectPeoplesAsync("group-1"))
                .ReturnsAsync(new List<global::DomainService.Entities.Project>());

            var result = await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("no_projects");
            _peopleRepository.Verify(r => r.GetPeoplesAsync(It.IsAny<GetPeoplesRequest>()), Times.Never);
        }

        [Fact]
        public async Task GetPeoplesAsync_NullSharedProjects_ReturnsNoProjectsError()
        {
            _projectRepository.Setup(r => r.GetProjectPeoplesAsync("group-1"))
                .ReturnsAsync((List<global::DomainService.Entities.Project>)null!);

            var result = await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1" });

            result.Errors.Should().ContainKey("no_projects");
        }

        [Fact]
        public async Task GetPeoplesAsync_GroupsPeopleByDetails()
        {
            _projectRepository.Setup(r => r.GetProjectPeoplesAsync("group-1"))
                .ReturnsAsync([new global::DomainService.Entities.Project { TenantId = "TENANT-1" }]);

            var details = new PeopleDetails { UserId = "user-1", Email = "invitee@example.com" };
            _peopleRepository.Setup(r => r.GetPeoplesAsync(It.IsAny<GetPeoplesRequest>()))
                .ReturnsAsync((new List<GetProjectPeople>
                {
                    new() { ItemId = "pp-1", TenantId = "TENANT-1", Enviroment = "dev", peopleDetails = details, IsCreator = true },
                    new() { ItemId = "pp-2", TenantId = "TENANT-2", Enviroment = "stg", peopleDetails = details, IsInvitationSent = true }
                }, 2, 1, true));

            var result = await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1" });

            result.IsSuccess.Should().BeTrue();
            result.IsOwner.Should().BeTrue();
            result.TotalCount.Should().Be(2);
            result.PeoplesTotalCount.Should().Be(1);
            result.Peoples.Should().HaveCount(1);
            result.Peoples[0].SharedEnviroments.Should().HaveCount(2);
            result.Peoples[0].SharedEnviroments.Select(e => e.Enviroment).Should().BeEquivalentTo(["dev", "stg"]);
        }

        [Fact]
        public async Task GetPeoplesAsync_RepositoryThrows_Rethrows()
        {
            _projectRepository.Setup(r => r.GetProjectPeoplesAsync("group-1"))
                .ReturnsAsync([new global::DomainService.Entities.Project { TenantId = "TENANT-1" }]);
            _peopleRepository.Setup(r => r.GetPeoplesAsync(It.IsAny<GetPeoplesRequest>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var act = async () => await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        #endregion
    }
}
