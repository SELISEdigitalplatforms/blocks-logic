using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Blocks.Genesis;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// SPEC8 ordering in the webhook service: workflow checks and auth before the body is read (A1), the body mode
    /// picks the intake, and files of a run that could not be created are discarded.
    /// </summary>
    public class WebhookAnyContentServiceTests : IDisposable
    {
        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IWorkflowAuthService> _auth = new();
        private readonly Mock<IWebhookIntake> _intake = new();
        private readonly WorkflowExecutionService _sut;

        public WebhookAnyContentServiceTests()
        {
            TestBlocksContext.Set("tenant-1");
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());
            _executions.Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .ReturnsAsync((WorkflowExecutionEntity e) => e);
            _sut = new WorkflowExecutionService(
                _workflows.Object,
                _executions.Object,
                Mock.Of<IMessageClient>(),
                Mock.Of<ILogger<WorkflowExecutionService>>(),
                Mock.Of<IWorkflowEngineService>(),
                _versions.Object,
                Mock.Of<IWorkflowNotificationService>(),
                _auth.Object,
                accessor.Object,
                Mock.Of<IDelegationGrantFactory>(),
                new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                Mock.Of<IInProcessGrantReleaser>(),
                _intake.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private void Published(string authType, string? bodyMode, bool isPublished = true)
        {
            var parameters = new BsonDocument { { "authType", authType }, { "httpResponseMode", "immediate" } };
            if (bodyMode is not null) parameters["bodyMode"] = bodyMode;
            var snapshot = new WorkflowEntity
            {
                TenantId = "tenant-1",
                ItemId = "wf-1",
                Nodes =
                [
                    new NodeEntity
                    {
                        Id = "hook", Name = "Webhook", Category = "trigger", Type = "webhook", Version = "v1",
                        Position = new Position(), Parameters = parameters,
                    }
                ],
            };
            _workflows.Setup(r => r.GetWorkflowAsync("tenant-1", "wf-1")).ReturnsAsync(new WorkflowEntity
            {
                TenantId = "tenant-1", ItemId = "wf-1", IsPublished = isPublished, PublishedVersionId = "ver-1",
            });
            _versions.Setup(r => r.GetWorkflowVersionAsync("tenant-1", "ver-1")).ReturnsAsync(new WorkflowVersionEntity
            {
                TenantId = "tenant-1", ItemId = "ver-1", WorkflowId = "wf-1", Name = "v1", Snapshot = snapshot,
            });
        }

        private static WebhookIntakeResult Result(params string[] fileIds) => new()
        {
            Items = new BsonArray { new BsonDocument("body", "x") },
            BodyFormat = "text",
            StoredFileIds = fileIds,
        };

        [Fact]
        public async Task Any_content_mode_uses_the_intake_and_stores_its_items_as_the_trigger_data()
        {
            Published("none", "anyContent");
            _intake.Setup(i => i.ReadAnyContentAsync(It.IsAny<HttpRequest>(), It.Is<WebhookIntakeSettings>(s => s.IsAnyContent),
                    "tenant-1", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result());
            WorkflowExecutionEntity? created = null;
            _executions.Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .Callback<WorkflowExecutionEntity>(e => created = e)
                .ReturnsAsync((WorkflowExecutionEntity e) => e);

            var response = await _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", WebhookRequests.Create("text/plain", "x"));

            response.Status.Should().Be("Queued");
            created!.TriggerMetadata!.TriggerData.ToJson().Should().Be(new BsonArray { new BsonDocument("body", "x") }.ToJson());
            _intake.Verify(i => i.ReadLegacyAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("json")]
        [InlineData("unknown")]
        public async Task Absent_or_unknown_body_mode_uses_the_legacy_reader(string? bodyMode)   // H1, C18
        {
            Published("none", bodyMode);
            _intake.Setup(i => i.ReadLegacyAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BsonArray { new BsonDocument("a", 1) });

            await _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", WebhookRequests.Json("{\"a\":1}"));

            _intake.Verify(i => i.ReadAnyContentAsync(It.IsAny<HttpRequest>(), It.IsAny<WebhookIntakeSettings>(),
                It.IsAny<string>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("anyContent")]
        [InlineData(null)]
        public async Task Failed_auth_answers_401_before_the_body_is_read(string? bodyMode)   // C1, A1
        {
            Published("blocksAuthentication", bodyMode);
            _auth.Setup(a => a.IsAuthenticated(It.IsAny<HttpRequest>(), "tenant-1")).ReturnsAsync(false);

            var act = () => _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", WebhookRequests.Create("text/plain", "{oops"));

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            _intake.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Unpublished_workflow_answers_its_status_without_reading_the_body()   // C19
        {
            Published("none", null, isPublished: false);

            var response = await _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", WebhookRequests.Create("text/plain", "{oops"));

            response.Status.Should().Be("Workflow is not published");
            _intake.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Authenticated_caller_is_handed_to_the_intake()   // H9
        {
            Published("blocksAuthentication", "anyContent");
            var caller = TestBlocksContext.Create("tenant-1", "user-7");
            _auth.Setup(a => a.IsAuthenticated(It.IsAny<HttpRequest>(), "tenant-1")).ReturnsAsync(true);
            _auth.Setup(a => a.BuildCallerContext(It.IsAny<HttpRequest>(), "tenant-1")).Returns(caller);
            _intake.Setup(i => i.ReadAnyContentAsync(It.IsAny<HttpRequest>(), It.IsAny<WebhookIntakeSettings>(),
                    "tenant-1", caller, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result());

            await _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", WebhookRequests.Create("text/plain", "x"));

            _intake.VerifyAll();
        }

        [Fact]
        public async Task Intake_refusal_propagates_with_its_status()
        {
            Published("none", "anyContent");
            _intake.Setup(i => i.ReadAnyContentAsync(It.IsAny<HttpRequest>(), It.IsAny<WebhookIntakeSettings>(),
                    It.IsAny<string>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(WebhookIntakeException.TooLarge("Request body exceeds 50 MiB"));

            var act = () => _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", WebhookRequests.Create("application/pdf", "x"));

            (await act.Should().ThrowAsync<WebhookIntakeException>()).Which.StatusCode.Should().Be(413);
            _executions.Verify(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()), Times.Never);
        }

        [Fact]
        public async Task Files_are_discarded_when_the_execution_cannot_be_created()
        {
            Published("none", "anyContent");
            var result = Result("file-1");
            _intake.Setup(i => i.ReadAnyContentAsync(It.IsAny<HttpRequest>(), It.IsAny<WebhookIntakeSettings>(),
                    It.IsAny<string>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
            _executions.Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>())).ThrowsAsync(new TimeoutException("mongo"));

            var act = () => _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", WebhookRequests.Create("application/pdf", "x"));

            await act.Should().ThrowAsync<InvalidOperationException>();
            _intake.Verify(i => i.DiscardAsync(result, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
