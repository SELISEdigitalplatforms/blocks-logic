using Cloud.DomainService.Models;
using Cloud.DomainService.Repositories;
using Cloud.DomainService.Requests;
using Cloud.DomainService.Responses;
using Cloud.DomainService.Services;
using FluentAssertions;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Cloud
{
    public class ApiEndpointConfigServiceTests : IDisposable
    {
        private readonly Mock<IApiEndpointConfigRepository> _repo = new();
        private readonly ApiEndpointConfigService _service;

        public ApiEndpointConfigServiceTests()
        {
            TestBlocksContext.Set("tenant-api", "user-api");
            _service = new ApiEndpointConfigService(_repo.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        [Fact]
        public async Task GetList_ReturnsPagedResponse()
        {
            _repo.Setup(r => r.GetListAsync(It.IsAny<GetApiEndpointConfigsRequest>()))
                .ReturnsAsync((new List<ApiEndpointConfigResponse> { new() }, 1L));

            var result = await _service.GetListAsync(new GetApiEndpointConfigsRequest { Page = 2, PageSize = 5 });

            result.TotalCount.Should().Be(1);
            result.Page.Should().Be(2);
            result.PageSize.Should().Be(5);
            result.Data.Should().HaveCount(1);
        }

        [Fact]
        public async Task Update_Success_ReturnsSuccess()
        {
            _repo.Setup(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var result = await _service.UpdateAsync(new UpdateApiEndpointConfigRequest { ProjectKey = "p", ItemId = "i" });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task Update_NoMatch_ReturnsFailureWithError()
        {
            _repo.Setup(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            var result = await _service.UpdateAsync(new UpdateApiEndpointConfigRequest { ProjectKey = "p", ItemId = "i" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("update_failed");
        }

        [Fact]
        public async Task BulkUpdate_ModifiesRecords_ReturnsSuccess()
        {
            _repo.Setup(r => r.BulkUpdateAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(3);

            var result = await _service.BulkUpdateAsync(new BulkUpdateApiEndpointConfigRequest
            {
                ProjectKey = "p",
                ItemIds = new List<string> { "a", "b" },
                IsCaptchaRequired = true,
                IsMfaRequired = true
            });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task BulkUpdate_DisableAll_PassesFalseFlags()
        {
            _repo.Setup(r => r.BulkUpdateAsync(It.IsAny<string>(), It.IsAny<List<string>>(), false, false, It.IsAny<string>()))
                .ReturnsAsync(2);

            var result = await _service.BulkUpdateAsync(new BulkUpdateApiEndpointConfigRequest
            {
                ProjectKey = "p",
                ItemIds = new List<string> { "a" },
                DisableAll = true,
                IsCaptchaRequired = true,
                IsMfaRequired = true
            });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.BulkUpdateAsync("p", It.IsAny<List<string>>(), false, false, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task BulkUpdate_NoMatch_ReturnsFailure()
        {
            _repo.Setup(r => r.BulkUpdateAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(0);

            var result = await _service.BulkUpdateAsync(new BulkUpdateApiEndpointConfigRequest { ProjectKey = "p", ItemIds = new List<string>() });

            result.IsSuccess.Should().BeFalse();
        }
    }
}
