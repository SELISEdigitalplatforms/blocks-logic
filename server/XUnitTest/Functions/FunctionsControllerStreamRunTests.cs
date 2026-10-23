using BlocksTemplate.Api.Controllers;
using Common.InternalService.Access;
using FluentAssertions;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The Studio's live view of a run's streamed answer: only a run of the caller's own tenant,
    /// always as SSE, and no held connection for a finished run that has nothing to send.
    /// </summary>
    public class FunctionsControllerStreamRunTests
    {
        private readonly Mock<IFunctionRunService> _runs = new();
        private readonly Mock<IFunctionStreamReader> _reader = new();
        private readonly FunctionsController _controller;

        public FunctionsControllerStreamRunTests()
        {
            _controller = new FunctionsController(
                new Mock<IFunctionService>().Object,
                new Mock<IFunctionDeploymentService>().Object,
                new Mock<IFunctionInvocationService>().Object,
                _runs.Object,
                new Mock<IFunctionBuildService>().Object,
                new Mock<IFunctionAuditService>().Object,
                new Mock<IEndpointAccessAuthorizer>().Object,
                new Mock<IFunctionPollTokenService>().Object,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FunctionsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        private void GivenRun(string status, bool streamExists)
        {
            _runs.Setup(r => r.GetAsync(It.IsAny<string>(), "run_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunDetailDto { Status = status });
            _reader.Setup(r => r.ExistsAsync("run_1")).ReturnsAsync(streamExists);
        }

        private Task<IActionResult> Stream() =>
            _controller.StreamRun(new RunIdRequestDto { RunId = "run_1" }, _reader.Object);

        [Fact]
        public async Task A_live_run_is_streamed_as_sse()
        {
            GivenRun("Running", streamExists: false);

            var result = await Stream();

            result.Should().BeOfType<FunctionStreamResult>().Which.Sse.Should().BeTrue();
        }

        [Fact]
        public async Task A_finished_run_that_streamed_is_replayed()
        {
            GivenRun("Succeeded", streamExists: true);

            (await Stream()).Should().BeOfType<FunctionStreamResult>().Which.RunId.Should().Be("run_1");
        }

        [Fact]
        public async Task A_finished_run_with_nothing_streamed_is_an_empty_stream_not_a_held_connection()
        {
            GivenRun("Failed", streamExists: false);

            (await Stream()).Should().BeOfType<EmptyResult>();
            _controller.Response.ContentType.Should().StartWith("text/event-stream");
        }

        [Fact]
        public async Task Another_tenants_run_is_not_found()
        {
            _runs.Setup(r => r.GetAsync(It.IsAny<string>(), "run_1", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionNotFoundException("run 'run_1' was not found"));

            var act = Stream;

            await act.Should().ThrowAsync<FunctionNotFoundException>();
            _reader.Verify(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
