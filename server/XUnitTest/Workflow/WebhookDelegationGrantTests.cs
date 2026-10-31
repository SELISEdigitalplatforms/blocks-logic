using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Blocks.Genesis;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// The "last" webhook mode runs in-process, so the delegation grant minted for the caller must still be
    /// in <see cref="DelegatedTokenContext"/> when the engine runs. It used to be set inside an async helper,
    /// where the AsyncLocal write was discarded on return and every Blocks-auth node failed with
    /// "No delegated Blocks token for this run".
    /// </summary>
    public class WebhookDelegationGrantTests : IDisposable
    {
        private static readonly string GrantId = "dg_" + new string('a', 64);

        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IWorkflowEngineService> _engine = new();
        private readonly Mock<IDelegationGrantFactory> _grants = new();
        private readonly Mock<IInProcessGrantReleaser> _releaser = new();
        private readonly WorkflowExecutionService _sut;

        public WebhookDelegationGrantTests()
        {
            TestBlocksContext.Set("tenant-1");
            DelegatedTokenContext.Clear();
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());
            _executions.Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .ReturnsAsync((WorkflowExecutionEntity e) => e);
            _sut = new WorkflowExecutionService(
                _workflows.Object,
                _executions.Object,
                Mock.Of<IMessageClient>(),
                Mock.Of<ILogger<WorkflowExecutionService>>(),
                _engine.Object,
                _versions.Object,
                Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IWorkflowAuthService>(),
                accessor.Object,
                _grants.Object,
                new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                _releaser.Object,
                Mock.Of<global::Workflow.DomainService.BlocksEvents.IBlocksEventCatalogService>());
        }

        public void Dispose()
        {
            DelegatedTokenContext.Clear();
            TestBlocksContext.Clear();
        }

        private void Published()
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
                        Parameters = new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" }, { "httpResponseData", "none" } },
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

        [Fact]
        public async Task Last_mode_runs_the_engine_with_the_minted_grant_in_scope()
        {
            Published();
            _grants.Setup(g => g.CreateForSendAsync(It.IsAny<TimeSpan?>())).Returns(async () =>
            {
                await Task.Yield(); // a real store round-trip completes asynchronously
                return GrantId;
            });
            string? grantSeenByEngine = null;
            _engine.Setup(e => e.RunNodeInProcessAsync(It.IsAny<AddExcuationNodeEvent>()))
                .Callback(() => grantSeenByEngine = DelegatedTokenContext.Current)
                .ReturnsAsync((WorkflowExecutionEntity?)null);

            using var body = JsonDocument.Parse("{}");
            await _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", body.RootElement.Clone());

            grantSeenByEngine.Should().Be(GrantId);
        }

        [Fact]
        public async Task Last_mode_releases_the_grant_after_the_run_returns()
        {
            Published();
            _grants.Setup(g => g.CreateForSendAsync(It.IsAny<TimeSpan?>())).ReturnsAsync(GrantId);
            var releasedBeforeRunEnded = false;
            _engine.Setup(e => e.RunNodeInProcessAsync(It.IsAny<AddExcuationNodeEvent>()))
                .Callback(() => releasedBeforeRunEnded = _releaser.Invocations.Count > 0)
                .ReturnsAsync((WorkflowExecutionEntity?)null);

            using var body = JsonDocument.Parse("{}");
            await _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", body.RootElement.Clone());

            releasedBeforeRunEnded.Should().BeFalse();
            _releaser.Verify(r => r.ReleaseAsync(GrantId), Times.Once);
            DelegatedTokenContext.Current.Should().BeNull();
        }

        [Fact]
        public async Task Last_mode_releases_the_grant_when_the_run_throws()
        {
            Published();
            _grants.Setup(g => g.CreateForSendAsync(It.IsAny<TimeSpan?>())).ReturnsAsync(GrantId);
            _engine.Setup(e => e.RunNodeInProcessAsync(It.IsAny<AddExcuationNodeEvent>()))
                .ThrowsAsync(new InvalidOperationException("engine failed"));

            using var body = JsonDocument.Parse("{}");
            try { await _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", body.RootElement.Clone()); }
            catch (InvalidOperationException) { /* whether the service rethrows is not what this test is about */ }

            _releaser.Verify(r => r.ReleaseAsync(GrantId), Times.Once);
        }
    }
}
