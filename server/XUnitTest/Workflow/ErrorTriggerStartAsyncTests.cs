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
    /// <summary>blocks-logic#380: the Worker reads the failed run and starts its alert's error workflow.</summary>
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
            SetFailedExecution();
            SetSnapshot(ErrorTriggerNode());

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

        private void SetFailedExecution(WorkflowExecutionMode mode = WorkflowExecutionMode.Production, string? alertWorkflowId = "wf-b") =>
            _executions.Setup(r => r.GetByIdAsync("exec-a", Tenant)).ReturnsAsync(new WorkflowExecutionEntity
            {
                Id = "exec-a", TenantId = Tenant, WorkflowId = "wf-a", WorkflowName = "Sync orders",
                ExecutionMode = mode,
                StartedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                FinishedAt = new DateTime(2026, 1, 1, 9, 0, 3, DateTimeKind.Utc),
                TriggerMetadata = new TriggerMetadata { TriggerType = "webhook" },
                WorkflowSnapshot = new WorkflowEntity
                {
                    TenantId = Tenant, ItemId = "wf-a", Nodes = [], Edges = [],
                    Settings = new WorkflowSettings
                    {
                        Alert = new WorkflowAlert { IsEnabled = true, Mode = WorkflowAlertMode.Workflow, WorkflowId = alertWorkflowId },
                    },
                },
            });

        private static NodeEntity ErrorTriggerNode(bool runOnTestMode = false) =>
            new()
            {
                Id = "err", Name = "Error Trigger", Type = LogicConstants.ErrorTriggerNodeType, Category = "trigger", Version = "v2", Position = new Position(),
                Parameters = new BsonDocument("runOnTestMode", runOnTestMode),
            };

        private static ErrorTriggerEvent Event() => new()
        {
            TenantId = Tenant,
            FailedWorkflowId = "wf-a",
            FailedExecutionId = "exec-a",
            Error = new ErrorTriggerEventError { Message = "timeout", NodeId = "n1", NodeName = "Postgres", NodeType = "dataAction" },
        };

        [Fact]
        public async Task H9_StartsOneProductionRunOfThePublishedVersion_AtItsErrorTrigger_WithThePayload()
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
                        { "workflowId", "wf-a" }, { "workflowName", "Sync orders" }, { "executionId", "exec-a" }, { "mode", "Production" },
                        { "startedAt", "2026-01-01T09:00:00Z" }, { "finishedAt", "2026-01-01T09:00:03Z" },
                        { "error", new BsonDocument { { "message", "timeout" }, { "nodeId", "n1" }, { "nodeName", "Postgres" }, { "nodeType", "dataAction" } } },
                    }
                },
            }).Should().BeTrue(payload.ToJson());
            _created.Context[LogicConstants.ExecutionInputKey].Equals(_created.TriggerMetadata.TriggerData).Should().BeTrue();
            _messages.Verify(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<AddExcuationNodeEvent>>(c =>
                c.ConsumerName == LogicConstants.NodeQueueFor("exec-b") && c.Payload.WorkflowId == "wf-b" && c.Payload.NodeId == "err")), Times.Once);
        }

        [Fact]
        public async Task ATestFailure_RunsTheErrorWorkflowsDraft_InTestMode_EvenWhenUnpublished()
        {
            SetFailedExecution(WorkflowExecutionMode.Test);
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-b")).ReturnsAsync(new WorkflowEntity
            {
                TenantId = Tenant, ItemId = "wf-b", IsPublished = false, Nodes = [ErrorTriggerNode(runOnTestMode: true)], Edges = [],
            });

            await _sut.ErrorTriggerStartAsync(Event());

            _created!.ExecutionMode.Should().Be(WorkflowExecutionMode.Test);
            _created.TriggerMetadata!.TriggerData![0]["execution"]["mode"].AsString.Should().Be("Test");
            _versions.Verify(v => v.GetWorkflowVersionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ATestFailure_StartsNothing_WhenRunOnTestModeIsOff()
        {
            SetFailedExecution(WorkflowExecutionMode.Test);
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-b")).ReturnsAsync(new WorkflowEntity
            {
                TenantId = Tenant, ItemId = "wf-b", IsPublished = true, PublishedVersionId = "ver-b", Nodes = [ErrorTriggerNode()], Edges = [],
            });

            await _sut.ErrorTriggerStartAsync(Event());

            _created.Should().BeNull();
        }

        [Fact]
        public async Task C7_AnErrorWorkflowThatIsGone_Unpublished_OrWithoutAnErrorTrigger_StartsNothing()
        {
            SetFailedExecution(alertWorkflowId: "wf-missing");
            await _sut.ErrorTriggerStartAsync(Event());
            _created.Should().BeNull();

            SetFailedExecution();
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-b")).ReturnsAsync(new WorkflowEntity { TenantId = Tenant, ItemId = "wf-b", IsPublished = false });
            await _sut.ErrorTriggerStartAsync(Event());
            _created.Should().BeNull();

            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-b")).ReturnsAsync(new WorkflowEntity { TenantId = Tenant, ItemId = "wf-b", IsPublished = true, PublishedVersionId = "ver-b" });
            SetSnapshot(new NodeEntity { Id = "hook", Name = "Webhook", Type = "webhook", Category = "trigger", Version = "v2", Position = new Position() });
            await _sut.ErrorTriggerStartAsync(Event());
            _created.Should().BeNull();
        }

        [Fact]
        public async Task AFailedRunThatIsGone_OrHasNoAlert_StartsNothing()
        {
            _executions.Setup(r => r.GetByIdAsync("exec-a", Tenant)).ReturnsAsync((WorkflowExecutionEntity?)null);
            await _sut.ErrorTriggerStartAsync(Event());
            _created.Should().BeNull();

            SetFailedExecution(alertWorkflowId: "");
            await _sut.ErrorTriggerStartAsync(Event());
            _created.Should().BeNull();
            _workflows.Verify(r => r.GetWorkflowAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
