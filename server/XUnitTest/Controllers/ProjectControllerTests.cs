using Api.Controllers;
using DomainService.Dtos;
using DomainService.Projects;
using FluentAssertions;
using Moq;

namespace XUnitTest.Controllers
{
    /// <summary>
    /// Unit tests for <see cref="ProjectController"/>. The controller is a thin pass-through over
    /// <see cref="IProjectManagementService"/>, so what is worth pinning is that each action hands
    /// the caller's request to the service unchanged and returns the service's own result rather
    /// than rewrapping it.
    /// </summary>
    public class ProjectControllerTests
    {
        private readonly Mock<IProjectManagementService> _service = new();
        private readonly ProjectController _controller;

        public ProjectControllerTests()
        {
            _controller = new ProjectController(_service.Object);
        }

        [Fact]
        public async Task Gets_ReturnsTheGroupedProjectsFromTheService()
        {
            var expected = new List<GroupedProjectsDto>
            {
                new() { TenantGroupId = "tg-1", IsShared = true },
                new() { TenantGroupId = "tg-2" },
            };
            _service.Setup(s => s.GetAllAsync(It.IsAny<GetProjectsRequest>())).ReturnsAsync(expected);

            var result = await _controller.Gets(new GetProjectsRequest());

            result.Should().BeSameAs(expected);
            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task Gets_PassesTheRequestToTheServiceUnchanged()
        {
            var request = new GetProjectsRequest { TenantGroupId = "tg-1" };
            _service.Setup(s => s.GetAllAsync(It.IsAny<GetProjectsRequest>()))
                    .ReturnsAsync([]);

            await _controller.Gets(request);

            _service.Verify(s => s.GetAllAsync(request), Times.Once);
        }

        [Fact]
        public async Task Gets_ReturnsAnEmptyListWhenTheServiceFindsNothing()
        {
            _service.Setup(s => s.GetAllAsync(It.IsAny<GetProjectsRequest>())).ReturnsAsync([]);

            var result = await _controller.Gets(new GetProjectsRequest());

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task Get_ReturnsTheCurrentProjectFromTheService()
        {
            var expected = new GetProjectResponse();
            _service.Setup(s => s.GetAsync()).ReturnsAsync(expected);

            var result = await _controller.Get();

            result.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task Get_TakesNoArgumentsSoItCannotBeScopedByTheCaller()
        {
            // The endpoint reads the project from the ambient context rather than from the
            // request, so the service call carries no caller supplied parameters at all.
            _service.Setup(s => s.GetAsync()).ReturnsAsync(new GetProjectResponse());

            await _controller.Get();

            _service.Verify(s => s.GetAsync(), Times.Once);
            _service.VerifyNoOtherCalls();
        }
    }
}
