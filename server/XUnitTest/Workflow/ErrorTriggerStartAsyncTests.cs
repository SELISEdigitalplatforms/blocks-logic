using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>blocks-logic#380: the Worker starts the alert's error workflow from an ErrorTriggerEvent.</summary>
    public class ErrorTriggerStartAsyncTests
    {
        private const string Tenant = "tenant-1";
        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IMessageClient> _messages = new();
        private readonly WorkflowExecutionService _sut;
        private WorkflowExecutionEntity? _created;

        public ErrorTriggerStartAsyncTests()
        {
            TestBlocksContext.Set(Tenant);
            _executions.Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .ReturnsAsync((WorkflowExecutionEntity e) => { e.Id = "exec-b"; _created = e; return e; });
            _messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>())).Returns(Task.CompletedTask);

            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-b")).ReturnsAsync(new WorkflowEntity
            {
                TenantId = Tenant, ItemId = "wf-b", IsPublished = true, PublishedVersionId = "ver-b",
            });
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-a")).ReturnsAsync(new WorkflowEntity
            {
                TenantId = Tenant, ItemId = "wf-a", Name = "Sync orders",
            });
            SetSnapshot(new NodeEntity { Id = "err", Name = "Error Trigger", Type = LogicConstants.ErrorTriggerNodeType, Category = "trigger", Version = "v2", Position = new Position() });

            _sut = new WorkflowExecutionService(
                _workflows.Object, _executions.Object, _messages.Object,
                NullLogger<WorkflowExecutionService>.Instance, Mock.Of<IWorkflowEngineService>(), _versions.Object,
                Mock.Of<IWorkflowNotificationService>(), Mock.Of<IWorkflowAuthService>(), Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IDelegationGrantFactory>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                Mock.Of<IInProcessGrantReleaser>());
        }

        private void SetSnapshot(params NodeEntity[] nodes) =>
            _versions.Setup(v => v.GetWorkflowVersionAsync(Tenant, "ver-b")).ReturnsAsync(new WorkflowVersionEntity
            {
                TenantId = Tenant, ItemId = "ver-b", WorkflowId = "wf-b", Name = "v1",
                Snapshot = new WorkflowEntity { TenantId = Tenant, ItemId = "wf-b", Nodes = [.. nodes], Edges = [] },
            });

        private static ErrorTriggerEvent Event(string target = "wf-b") => new()
        {
            TenantId = Tenant,
            FailedWorkflowId = "wf-a",
            FailedExecutionId = "exec-a",
            AttemptNumber = 2,
            ExecutionMode = WorkflowExecutionMode.Production,
            StartedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            FinishedAt = new DateTime(2026, 1, 1, 9, 0, 3, DateTimeKind.Utc),
            Error = new ErrorTriggerEventError { Message = "timeout", NodeId = "n1", NodeName = "Postgres", NodeType = "dataAction" },
            Alert = new ErrorTriggerEventAlert { WorkflowId = target },
        };

        [Fact]
        public async Task H9_StartsOneProductionRunOfTheTarget_AtItsErrorTrigger_WithThePayload()
        {
            await _sut.ErrorTriggerStartAsync(Event());

            _created.Should().NotBeNull();
            _created!.ExecutionMode.Should().Be(WorkflowExecutionMode.Production);
            _created.TriggerMetadata!.TriggerType.Should().Be(LogicConstants.ErrorTriggerNodeType);
            _created.TriggerMetadata.TriggerNodeId.Should().Be("err");
            _created.ActiveNodeIds.Should().Equal("err");
            var payload = _created.TriggerMetadata.TriggerData![0].AsBsonDocument;
            (payload == new BsonDocument
            {
                { "execution", new BsonDocument
                    {
                        { "id", "exec-a" }, { "attemptNumber", 2 }, { "mode", "Production" },
                        { "startedAt", "2026-01-01T09:00:00Z" }, { "finishedAt", "2026-01-01T09:00:03Z" },
                        { "error", new BsonDocument { { "message", "timeout" }, { "nodeId", "n1" }, { "nodeName", "Postgres" }, { "nodeType", "dataAction" } } },
                    }
                },
                { "workflow", new BsonDocument { { "id", "wf-a" }, { "name", "Sync orders" } } },
            }).Should().BeTrue(payload.ToJson());
            _created.Context[LogicConstants.ExecutionInputKey].Equals(_created.TriggerMetadata.TriggerData).Should().BeTrue();
            _messages.Verify(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<AddExcuationNodeEvent>>(c =>
                c.ConsumerName == LogicConstants.NodeQueueFor("exec-b") && c.Payload.NodeId == "err")), Times.Once);
        }

        [Fact]
        public async Task C6_ARedeliveredEvent_StartsNothing()
        {
            _executions.Setup(r => r.ErrorRunExistsAsync(Tenant, "wf-b", "exec-a", 2)).ReturnsAsync(true);

            await _sut.ErrorTriggerStartAsync(Event());

            _created.Should().BeNull();
        }

        [Fact]
        public async Task C7_ATargetThatIsGone_Unpublished_OrWithoutAnErrorTrigger_StartsNothing()
        {
            await _sut.ErrorTriggerStartAsync(Event("wf-missing"));
            _created.Should().BeNull();

            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-b")).ReturnsAsync(new WorkflowEntity { TenantId = Tenant, ItemId = "wf-b", IsPublished = false });
            await _sut.ErrorTriggerStartAsync(Event());
            _created.Should().BeNull();

            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-b")).ReturnsAsync(new WorkflowEntity { TenantId = Tenant, ItemId = "wf-b", IsPublished = true, PublishedVersionId = "ver-b" });
            SetSnapshot(new NodeEntity { Id = "hook", Name = "Webhook", Type = "webhook", Category = "trigger", Version = "v2", Position = new Position() });
            await _sut.ErrorTriggerStartAsync(Event());
            _created.Should().BeNull();
        }

        [Fact]
        public async Task C8_ADeletedFailedWorkflow_StillAlerts_WithAnEmptyName()
        {
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-a")).ReturnsAsync((WorkflowEntity)null!);

            await _sut.ErrorTriggerStartAsync(Event());

            _created!.TriggerMetadata!.TriggerData![0]["workflow"]["name"].AsString.Should().BeEmpty();
        }

        [Fact]
        public async Task AnEventWithoutATarget_StartsNothing()
        {
            await _sut.ErrorTriggerStartAsync(Event(target: ""));

            _created.Should().BeNull();
            _workflows.Verify(r => r.GetWorkflowAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
