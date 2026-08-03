using System.Text.Json;
using Blocks.Genesis;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Utilities.Api.Controllers;
using XUnitTest.TestHelpers;

namespace XUnitTest.Controllers
{
    public class WorkflowControllerTests : IDisposable
    {
        private readonly Mock<IWorkflowService> _workflowService = new();
        private readonly Mock<IWorkflowVersionService> _versionService = new();
        private readonly Mock<IWorkflowExecutionService> _executionService = new();
        private readonly WorkflowController _controller;

        public WorkflowControllerTests()
        {
            TestBlocksContext.Set("tenant-abc");
            _controller = new WorkflowController(_workflowService.Object, _versionService.Object, _executionService.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private static JsonElement EmptyJson()
        {
            using var doc = JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }

        [Fact]
        public async Task GetAll_ReturnsOk_WithServiceResult()
        {
            var expected = new WorkflowGetsResponseDto();
            _workflowService.Setup(s => s.GetAllAsync("tenant-abc", It.IsAny<WorkflowGetsRequestDto>()))
                .ReturnsAsync(expected);

            var result = await _controller.GetAll(new WorkflowGetsRequestDto());

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task Get_ReturnsOk()
        {
            var expected = new WorkflowGetResponseDto();
            _workflowService.Setup(s => s.GetAsync("tenant-abc", It.IsAny<WorkflowGetRequestDto>()))
                .ReturnsAsync(expected);

            var result = await _controller.Get(new WorkflowGetRequestDto { WorkflowId = "wf" });

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task Create_Returns201()
        {
            var expected = new BaseMutationResponse();
            _workflowService.Setup(s => s.CreateAsync("tenant-abc", It.IsAny<WorkflowCreateRequestDto>()))
                .ReturnsAsync(expected);

            var result = await _controller.Create(new WorkflowCreateRequestDto { Name = "wf" });

            var obj = result.Should().BeOfType<ObjectResult>().Which;
            obj.StatusCode.Should().Be(StatusCodes.Status201Created);
            obj.Value.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task Duplicate_Returns201()
        {
            _workflowService.Setup(s => s.DuplicateAsync("tenant-abc", It.IsAny<WorkflowDuplicateRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.Duplicate(new WorkflowDuplicateRequestDto { Name = "wf", WorkflowId = "wf" });

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
        }

        [Fact]
        public async Task Update_ReturnsOk()
        {
            _workflowService.Setup(s => s.UpdateAsync("tenant-abc", It.IsAny<WorkflowUpdateRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.Update(new WorkflowUpdateRequestDto { ItemId = "wf" });

            result.Should().BeOfType<OkObjectResult>();
            _workflowService.Verify(s => s.UpdateAsync("tenant-abc", It.IsAny<WorkflowUpdateRequestDto>()), Times.Once);
        }

        [Fact]
        public async Task Delete_ReturnsOk()
        {
            _workflowService.Setup(s => s.DeleteAsync("tenant-abc", It.IsAny<WorkflowDeleteRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.Delete(new WorkflowDeleteRequestDto { Id = "wf" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task CreateVersion_Returns201()
        {
            _versionService.Setup(s => s.CreateVersionAsync("tenant-abc", It.IsAny<WorkflowVersionCreateRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.CreateVersion(new WorkflowVersionCreateRequestDto { WorkflowId = "wf", Name = "v" });

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
        }

        [Fact]
        public async Task UpdateVersion_ReturnsOk()
        {
            _versionService.Setup(s => s.UpdateVersionAsync("tenant-abc", It.IsAny<WorkflowVersionUpdateRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.UpdateVersion(new WorkflowVersionUpdateRequestDto { VersionId = "v", Name = "v" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetVersions_ReturnsOk()
        {
            _versionService.Setup(s => s.GetWorkflowVersionsAsync("tenant-abc", It.IsAny<WorkflowGetVersionsRequestDto>()))
                .ReturnsAsync(new WorkflowGetVersionsResponseDto());

            var result = await _controller.GetVersions(new WorkflowGetVersionsRequestDto { WorkflowId = "wf" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetWorkflowByVersion_ReturnsOk()
        {
            _workflowService.Setup(s => s.GetWorkflowByVersionAsync("tenant-abc", It.IsAny<GetWorkflowByVersionRequestDto>()))
                .ReturnsAsync(new GetWorkflowByVersionResponseDto());

            var result = await _controller.GetWorkflowByVersion(new GetWorkflowByVersionRequestDto { WorkflowId = "wf", VersionId = "v" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task PublishNewVersion_ReturnsOk()
        {
            _workflowService.Setup(s => s.PublishNewVersionAsync("tenant-abc", It.IsAny<WorkflowPublishNewVersionRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.PublishNewVersion(new WorkflowPublishNewVersionRequestDto { WorkflowId = "wf", Name = "v" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task PublishVersion_ReturnsOk()
        {
            _workflowService.Setup(s => s.PublishVersionAsync("tenant-abc", It.IsAny<WorkflowPublishVersionRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.PublishVersion(new WorkflowPublishVersionRequestDto { WorkflowId = "wf" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Unpublish_ReturnsOk()
        {
            _workflowService.Setup(s => s.UnpublishAsync("tenant-abc", It.IsAny<WorkflowUnpublishRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.Unpublish(new WorkflowUnpublishRequestDto { WorkflowId = "wf" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Restore_ReturnsOk()
        {
            _workflowService.Setup(s => s.RestoreAsync("tenant-abc", It.IsAny<WorkflowRestoreRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.Restore(new WorkflowRestoreRequestDto { WorkflowId = "wf", VersionId = "v" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Webhook_Success_ReturnsOk()
        {
            _executionService.Setup(s => s.TriggerWebhookAsync("wf1", "wh1", "proj1", It.IsAny<JsonElement>()))
                .ReturnsAsync(new WorkflowWebhookResponseDto());

            var result = await _controller.Webhook("proj1", "wf1", "wh1", EmptyJson());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Webhook_Unauthorized_Returns401()
        {
            _executionService.Setup(s => s.TriggerWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            var result = await _controller.Webhook("proj1", "wf1", "wh1", EmptyJson());

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task TestWebhook_Success_ReturnsOk()
        {
            _executionService.Setup(s => s.TriggerTestWebhookAsync("wf1", "wh1", "proj1", It.IsAny<JsonElement>()))
                .ReturnsAsync(new WorkflowWebhookResponseDto());

            var result = await _controller.TestWebhook("proj1", "wf1", "wh1", EmptyJson());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task TestWebhook_Unauthorized_Returns401()
        {
            _executionService.Setup(s => s.TriggerTestWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            var result = await _controller.TestWebhook("proj1", "wf1", "wh1", EmptyJson());

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task StepExecute_ReturnsOk()
        {
            _executionService.Setup(s => s.StepExecuteAsync("tenant-abc", It.IsAny<StepExecuteRequestDto>()))
                .ReturnsAsync(new StepExecuteResponseDto());

            var result = await _controller.StepExecute(new StepExecuteRequestDto { WorkflowId = "wf", NodeId = "n" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task TriggerListener_ReturnsOk()
        {
            _workflowService.Setup(s => s.TriggerListenerAsync("tenant-abc", It.IsAny<TriggerListenerRequestDto>()))
                .ReturnsAsync(new BaseMutationResponse());

            var result = await _controller.TriggerListener(new TriggerListenerRequestDto { WorkflowId = "wf" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetExecutions_ReturnsOk()
        {
            _executionService.Setup(s => s.GetExecutionsByWorkflowIdAsync("tenant-abc", It.IsAny<WorkflowExecutionsGetRequestDto>()))
                .ReturnsAsync(new WorkflowExecutionsGetResponseDto());

            var result = await _controller.GetExecutions(new WorkflowExecutionsGetRequestDto { WorkflowId = "wf" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetExecution_ReturnsOk()
        {
            _executionService.Setup(s => s.GetExecutionByIdAsync("tenant-abc", It.IsAny<WorkflowExecutionGetRequestDto>()))
                .ReturnsAsync(new WorkflowExecutionGetResponseDto());

            var result = await _controller.GetExecution(new WorkflowExecutionGetRequestDto { ExecutionId = "e" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task LastSuccessfullExecution_ReturnsOk()
        {
            _executionService.Setup(s => s.LastSuccessfullExecutionAsync("tenant-abc", It.IsAny<LastSuccessfullExecutionRequestDto>()))
                .ReturnsAsync(new WorkflowExecutionGetResponseDto());

            var result = await _controller.LastSuccessfullExecution(new LastSuccessfullExecutionRequestDto { WorkflowId = "wf" });

            result.Should().BeOfType<OkObjectResult>();
        }
    }
}
