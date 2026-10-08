using System.Text.Json;
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

namespace XUnitTest.Workflow
{
    /// <summary>
    /// The webhook service refuses a body that is not an object / array of objects with
    /// <see cref="InvalidWebhookBodyException"/> (400), before any execution row exists, and only after
    /// the trigger's own auth (an unauthenticated caller still gets 401).
    /// </summary>
    public class WebhookInvalidBodyServiceTests : IDisposable
    {
        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IWorkflowAuthService> _auth = new();
        private readonly WorkflowExecutionService _sut;

        public WebhookInvalidBodyServiceTests()
        {
            TestBlocksContext.Set("tenant-1");
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());
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
                new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance));
        }

        public void Dispose() => TestBlocksContext.Clear();

        private void Published(string authType)
        {
            var snapshot = new WorkflowEntity
            {
                TenantId = "tenant-1",
                ItemId = "wf-1",
                Nodes =
                [
                    new NodeEntity
                    {
                        Id = "hook",
                        Name = "Webhook",
                        Category = "trigger",
                        Type = "webhook",
                        Version = "v1",
                        Position = new Position(),
                        Parameters = new BsonDocument { { "authType", authType } },
                    }
                ],
            };
            _workflows.Setup(r => r.GetWorkflowAsync("tenant-1", "wf-1")).ReturnsAsync(new WorkflowEntity
            {
                TenantId = "tenant-1", ItemId = "wf-1", IsPublished = true, PublishedVersionId = "ver-1",
            });
            _versions.Setup(r => r.GetWorkflowVersionAsync("tenant-1", "ver-1")).ReturnsAsync(new WorkflowVersionEntity
            {
                TenantId = "tenant-1", ItemId = "ver-1", WorkflowId = "wf-1", Name = "v1", Snapshot = snapshot,
            });
        }

        private static JsonElement Json(string raw)
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }

        [Theory]
        [InlineData("5")]
        [InlineData("\"x\"")]
        [InlineData("[1,2]")]
        [InlineData("[{\"a\":1},\"b\"]")]
        public async Task Bad_body_throws_InvalidWebhookBodyException_and_creates_no_execution(string raw)
        {
            Published("none");

            var act = () => _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", Json(raw));

            (await act.Should().ThrowAsync<InvalidWebhookBodyException>())
                .Which.Message.Should().StartWith("Body must be a JSON object or an array of objects");
            _executions.Verify(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()), Times.Never);
        }

        [Fact]
        public async Task Unauthenticated_caller_gets_401_before_the_body_is_checked()
        {
            Published("blocksAuthentication");
            _auth.Setup(a => a.IsAuthenticated(It.IsAny<HttpRequest>(), "tenant-1")).ReturnsAsync(false);

            var act = () => _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", Json("5"));

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }
}
