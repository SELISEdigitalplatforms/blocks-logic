using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Services;
using Utilities.Api.Controllers;
using XUnitTest.TestHelpers;

namespace XUnitTest.Proxy
{
    public class ProxyControllerTests : IDisposable
    {
        private readonly Mock<IProxyService> _proxyService = new();
        private readonly Mock<IProxyVersionService> _versionService = new();
        private readonly Mock<IProxyTestService> _testService = new();
        private readonly Mock<IProxyExecutionService> _executionService = new();
        private readonly ProxyController _controller;

        public ProxyControllerTests()
        {
            TestBlocksContext.Set("tenant-abc");
            _controller = new ProxyController(
                _proxyService.Object, _versionService.Object, _testService.Object, _executionService.Object);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static int StatusOf(IActionResult result) =>
            result switch
            {
                ObjectResult obj => obj.StatusCode ?? 200,
                StatusCodeResult code => code.StatusCode,
                _ => throw new InvalidOperationException($"Unexpected result type {result.GetType().Name}"),
            };

        [Fact]
        public async Task GetAll_ReturnsOk_WithServiceResult()
        {
            var expected = new ProxyGetAllResponseDto();
            _proxyService.Setup(s => s.GetAllAsync("tenant-abc", It.IsAny<ProxyGetAllRequestDto>())).ReturnsAsync(expected);

            var result = await _controller.GetAll(new ProxyGetAllRequestDto());

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task Get_ReturnsOk_WithServiceResult()
        {
            var expected = new ProxyGetResponseDto();
            _proxyService.Setup(s => s.GetAsync("tenant-abc", It.IsAny<ProxyGetRequestDto>())).ReturnsAsync(expected);

            var result = await _controller.Get(new ProxyGetRequestDto { ItemId = "p1" });

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task Create_Returns201_WhenServiceSucceeds()
        {
            _proxyService.Setup(s => s.CreateAsync("tenant-abc", It.IsAny<ProxyCreateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1", 201));

            var result = await _controller.Create(new ProxyCreateRequestDto());

            StatusOf(result).Should().Be(201);
        }

        [Fact]
        public async Task Create_Returns400_OnValidationFailure()
        {
            _proxyService.Setup(s => s.CreateAsync("tenant-abc", It.IsAny<ProxyCreateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(400, "PROXY_VALIDATION", "bad"));

            var result = await _controller.Create(new ProxyCreateRequestDto());

            StatusOf(result).Should().Be(400);
        }

        [Fact]
        public async Task Create_Returns409_OnSlugConflict()
        {
            _proxyService.Setup(s => s.CreateAsync("tenant-abc", It.IsAny<ProxyCreateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(409, "PROXY_SLUG_CONFLICT", "exists"));

            var result = await _controller.Create(new ProxyCreateRequestDto());

            StatusOf(result).Should().Be(409);
        }

        [Fact]
        public async Task Update_Returns200_WhenServiceSucceeds()
        {
            _proxyService.Setup(s => s.UpdateAsync("tenant-abc", It.IsAny<ProxyUpdateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1"));

            var result = await _controller.Update(new ProxyUpdateRequestDto { ItemId = "p1" });

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task Update_Returns404_WhenServiceReportsNotFound()
        {
            _proxyService.Setup(s => s.UpdateAsync("tenant-abc", It.IsAny<ProxyUpdateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(404, "PROXY_NOT_FOUND", "missing"));

            var result = await _controller.Update(new ProxyUpdateRequestDto { ItemId = "p1" });

            StatusOf(result).Should().Be(404);
        }

        [Fact]
        public async Task Toggle_Returns200_WhenServiceSucceeds()
        {
            _proxyService.Setup(s => s.ToggleAsync("tenant-abc", It.IsAny<ProxyToggleRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1"));

            var result = await _controller.Toggle(new ProxyToggleRequestDto { ItemId = "p1", Enabled = false });

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task Delete_Returns200_WhenServiceSucceeds()
        {
            _proxyService.Setup(s => s.DeleteAsync("tenant-abc", It.IsAny<ProxyDeleteRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1"));

            var result = await _controller.Delete(new ProxyDeleteRequestDto { ItemId = "p1" });

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task Delete_Returns404_WhenServiceReportsNotFound()
        {
            _proxyService.Setup(s => s.DeleteAsync("tenant-abc", It.IsAny<ProxyDeleteRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(404, "PROXY_NOT_FOUND", "missing"));

            var result = await _controller.Delete(new ProxyDeleteRequestDto { ItemId = "p1" });

            StatusOf(result).Should().Be(404);
        }

        [Fact]
        public async Task GetVersions_Returns200_WhenServiceSucceeds()
        {
            _versionService.Setup(s => s.GetVersionsAsync("tenant-abc", It.IsAny<ProxyGetVersionsRequestDto>()))
                .ReturnsAsync(new ProxyGetVersionsResponseDto());

            var result = await _controller.GetVersions(new ProxyGetVersionsRequestDto { ProxyId = "p1" });

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task GetVersions_Returns404_WhenServiceReportsUnknownProxy()
        {
            _versionService.Setup(s => s.GetVersionsAsync("tenant-abc", It.IsAny<ProxyGetVersionsRequestDto>()))
                .ReturnsAsync(new ProxyGetVersionsResponseDto { HttpStatus = 404, Code = "PROXY_NOT_FOUND" });

            var result = await _controller.GetVersions(new ProxyGetVersionsRequestDto { ProxyId = "p1" });

            StatusOf(result).Should().Be(404);
        }

        [Fact]
        public async Task Revert_Returns200_WhenServiceSucceeds()
        {
            _versionService.Setup(s => s.RevertAsync("tenant-abc", It.IsAny<ProxyRevertRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1"));

            var result = await _controller.Revert(new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v1" });

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task Revert_Returns409_WhenProxyDeleted()
        {
            _versionService.Setup(s => s.RevertAsync("tenant-abc", It.IsAny<ProxyRevertRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(409, "PROXY_DELETED", "gone"));

            var result = await _controller.Revert(new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v1" });

            StatusOf(result).Should().Be(409);
        }

        [Fact]
        public async Task Test_Returns200_WithServiceResult_WhenSuccessful()
        {
            var payload = new ProxyTestResponseDto { Ok = true, Status = 200, Outcome = "Success" };
            _testService.Setup(s => s.TestAsync("tenant-abc", It.IsAny<string?>(), It.IsAny<ProxyTestRequestDto>()))
                .ReturnsAsync(ProxyTestOutcome.Ok(payload));

            var result = await _controller.Test(new ProxyTestRequestDto { ProxyId = "p1", Method = "GET" });

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(payload);
        }

        [Fact]
        public async Task Test_Returns400_WithCodeAndErrors_OnValidationFailure()
        {
            _testService.Setup(s => s.TestAsync("tenant-abc", It.IsAny<string?>(), It.IsAny<ProxyTestRequestDto>()))
                .ReturnsAsync(ProxyTestOutcome.Validation(new Dictionary<string, string> { ["method"] = "PUT is not enabled for this proxy." }));

            var result = await _controller.Test(new ProxyTestRequestDto { ProxyId = "p1", Method = "PUT" });

            StatusOf(result).Should().Be(400);
        }

        // ---------- Phase 3 : GetExecutions / GetExecution / GetOverview / ExportExecutionsCsv ----------

        [Fact]
        public async Task GetExecutions_ReturnsServiceHttpStatus()
        {
            _executionService.Setup(s => s.GetExecutionsAsync("tenant-abc", It.IsAny<ProxyGetExecutionsRequestDto>()))
                .ReturnsAsync(new ProxyGetExecutionsResponseDto { HttpStatus = 404, Code = "PROXY_NOT_FOUND" });

            var result = await _controller.GetExecutions(new ProxyGetExecutionsRequestDto { ProxyId = "ghost" });

            StatusOf(result).Should().Be(404);
        }

        [Fact]
        public async Task GetExecution_Returns200_WithNullData_OnMismatch()
        {
            _executionService.Setup(s => s.GetExecutionAsync("tenant-abc", It.IsAny<ProxyGetExecutionRequestDto>()))
                .ReturnsAsync(new ProxyGetExecutionResponseDto { Data = null });

            var result = await _controller.GetExecution(new ProxyGetExecutionRequestDto { ItemId = "e1", ProxyId = "p1" });

            StatusOf(result).Should().Be(200);
            result.Should().BeOfType<ObjectResult>()
                .Which.Value.Should().BeOfType<ProxyGetExecutionResponseDto>()
                .Which.Data.Should().BeNull();
        }

        [Fact]
        public async Task GetOverview_ReturnsServiceHttpStatus()
        {
            _executionService.Setup(s => s.GetOverviewAsync("tenant-abc", It.IsAny<ProxyGetOverviewRequestDto>()))
                .ReturnsAsync(new ProxyGetOverviewResponseDto { Data = new ProxyOverviewDto() });

            var result = await _controller.GetOverview(new ProxyGetOverviewRequestDto { ProxyId = "p1" });

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task ExportExecutionsCsv_Returns_CsvFile_WithTruncationHeader()
        {
            _controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            };
            _executionService.Setup(s => s.ExportExecutionsCsvAsync("tenant-abc", It.IsAny<ProxyExportExecutionsRequestDto>()))
                .ReturnsAsync(ProxyCsvExportResult.Ok(new byte[] { 1, 2, 3 }, "proxy-p-logs-20260907T000000.csv", truncated: true));

            var result = await _controller.ExportExecutionsCsv(new ProxyExportExecutionsRequestDto { ProxyId = "p1" });

            var file = result.Should().BeOfType<FileContentResult>().Which;
            file.ContentType.Should().Be("text/csv; charset=utf-8");
            file.FileDownloadName.Should().Be("proxy-p-logs-20260907T000000.csv");
            _controller.Response.Headers["X-Proxy-Export-Truncated"].ToString().Should().Be("true");
        }

        [Fact]
        public async Task ExportExecutionsCsv_Returns400_OnValidationFailure()
        {
            _controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            };
            _executionService.Setup(s => s.ExportExecutionsCsvAsync("tenant-abc", It.IsAny<ProxyExportExecutionsRequestDto>()))
                .ReturnsAsync(ProxyCsvExportResult.Failure(400, "PROXY_VALIDATION", "bad",
                    new Dictionary<string, string> { ["statusClass"] = "Must be one of all, 2xx, 4xx, 5xx." }));

            var result = await _controller.ExportExecutionsCsv(new ProxyExportExecutionsRequestDto { ProxyId = "p1", StatusClass = "3xx" });

            StatusOf(result).Should().Be(400);
        }
    }
}
