using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// blocks-logic#380: a failed production attempt sends one ErrorTriggerEvent when its workflow has an
    /// enabled alert, and nothing (no message, no settings read) in every other case.
    /// </summary>
    public class ErrorTriggerAlertTests
    {
        private sealed class FailingExecutor : INodeExecutor
        {
            public string NodeType => "fn";
            public string Version => "v1";
            public Task<NodeExecutionResult> RunAsync(NodeExecutionContext context) =>
                Task.FromResult(NodeExecutionResult.Failed("timeout"));
        }

        private sealed record Harness(
            WorkflowEngineService Engine,
            Mock<IWorkflowRepository> Workflows,
            List<ErrorTriggerEvent> Sent,
            Func<string?> StoredError);

        private static readonly WorkflowSettings EnabledAlert = new()
        {
            Alert = new WorkflowAlert { IsEnabled = true, Mode = WorkflowAlertMode.Workflow, WorkflowId = "wf-b" },
        };

        private static Harness Build(
            WorkflowExecutionMode mode = WorkflowExecutionMode.Production,
            WorkflowSettings? settings = null,
            WorkflowExecutionStatus statusBefore = WorkflowExecutionStatus.Running,
            string triggerType = "webhook",
            bool settingsReadThrows = false)
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1",
                TenantId = "t1",
                WorkflowId = "wf-a",
                WorkflowName = "Sync orders",
                ExecutionMode = mode,
                Status = statusBefore,
                AttemptNumber = 2,
                StartedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                TriggerMetadata = new TriggerMetadata { TriggerType = triggerType },
                WorkflowSnapshot = new WorkflowEntity
                {
                    TenantId = "t1",
                    Nodes = [new NodeEntity { Id = "n1", Name = "Postgres", Category = "action", Type = "fn", Version = "v1", Position = new Position() }],
                    Edges = [],
                    TestMeta = new TestWorkflowMeta(),
                },
            };
            string? error = null;
            var repository = new Mock<IWorkflowExecutionRepository> { DefaultValue = DefaultValue.Empty };
            repository.Setup(r => r.GetByIdAsync("exec-1", "t1")).ReturnsAsync(execution);
            repository.Setup(r => r.GetItemsByNodeIdsAsync(It.IsAny<string>(), It.IsAny<List<Dictionary<string, string>>>(), It.IsAny<string>()))
                .ReturnsAsync(new List<WorkflowItemExecutionEntity>());
            repository.Setup(r => r.TryAddFirstNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            repository.Setup(r => r.TryAddNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            repository.Setup(r => r.AtomicUpdateNodeExecutionFailedAsync("exec-1", "t1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Dictionary<string, int>>()))
                .Callback((string _, string _, string _, string e, int _, Dictionary<string, int> _) => error = e)
                .Returns(Task.CompletedTask);

            var workflows = new Mock<IWorkflowRepository>();
            if (settingsReadThrows)
                workflows.Setup(r => r.GetWorkflowSettingsAsync("t1", "wf-a")).ThrowsAsync(new TimeoutException("mongo"));
            else
                workflows.Setup(r => r.GetWorkflowSettingsAsync("t1", "wf-a")).ReturnsAsync(settings);

            var sent = new List<ErrorTriggerEvent>();
            var messages = new Mock<IMessageClient>();
            messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<ErrorTriggerEvent>>()))
                .Callback((ConsumerMessage<ErrorTriggerEvent> m) =>
                {
                    m.ConsumerName.Should().Be(LogicConstants.ErrorTriggerQueue);
                    sent.Add(m.Payload);
                })
                .Returns(Task.CompletedTask);

            var engine = new WorkflowEngineService(repository.Object, [new FailingExecutor()], messages.Object,
                NullLogger<WorkflowEngineService>.Instance, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IServiceProvider>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                workflows.Object);
            return new Harness(engine, workflows, sent, () => error);
        }

        private static Task Run(Harness h) =>
            h.Engine.RunNodeAsync(new AddExcuationNodeEvent { TenantId = "t1", WorkflowId = "wf-a", WorkflowExecutionId = "exec-1", NodeId = "n1" });

        [Fact]
        public async Task H8_AProductionFailureWithAnEnabledAlert_SendsOneEvent()
        {
            var h = Build(settings: EnabledAlert);

            await Run(h);

            h.Sent.Should().ContainSingle();
            var sent = h.Sent[0];
            sent.Should().BeEquivalentTo(new
            {
                TenantId = "t1",
                FailedWorkflowId = "wf-a",
                FailedExecutionId = "exec-1",
                AttemptNumber = 2,
                ExecutionMode = WorkflowExecutionMode.Production,
                StartedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                Error = new { Message = "timeout", NodeId = "n1", NodeName = "Postgres", NodeType = "fn" },
                Alert = new { Mode = WorkflowAlertMode.Workflow, WorkflowId = "wf-b" },
            });
            sent.FinishedAt.Should().NotBeNull();
        }

        public static TheoryData<WorkflowSettings?> NoAlert => new()
        {
            null,
            new WorkflowSettings(),
            new WorkflowSettings { Alert = new WorkflowAlert { IsEnabled = false, WorkflowId = "wf-b" } },
            new WorkflowSettings { Alert = new WorkflowAlert { IsEnabled = true, WorkflowId = " " } },
            new WorkflowSettings { Alert = new WorkflowAlert { IsEnabled = true, Mode = (WorkflowAlertMode)2, WorkflowId = "wf-b" } },
        };

        [Theory]
        [MemberData(nameof(NoAlert))]
        public async Task C1_NoUsableAlert_SendsNothing_AfterOneSettingsRead(WorkflowSettings? settings)
        {
            var h = Build(settings: settings);

            await Run(h);

            h.Sent.Should().BeEmpty();
            h.Workflows.Verify(r => r.GetWorkflowSettingsAsync("t1", "wf-a"), Times.Once);
        }

        [Fact]
        public async Task C2_ATestRun_NeverReadsSettingsOrSends()
        {
            var h = Build(mode: WorkflowExecutionMode.Test, settings: EnabledAlert);

            await Run(h);

            h.Sent.Should().BeEmpty();
            h.Workflows.Verify(r => r.GetWorkflowSettingsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task C3_ARunStartedByAnErrorTrigger_NeverAlerts()
        {
            var h = Build(settings: EnabledAlert, triggerType: LogicConstants.ErrorTriggerNodeType);

            await Run(h);

            h.Sent.Should().BeEmpty();
            h.Workflows.Verify(r => r.GetWorkflowSettingsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task C4_ARunAnotherBranchAlreadyFailed_DoesNotAlertAgain()
        {
            var h = Build(settings: EnabledAlert, statusBefore: WorkflowExecutionStatus.Failed);

            await Run(h);

            h.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task C5_ASettingsReadFailure_IsSwallowed_AndTheRunStillFails()
        {
            var h = Build(settingsReadThrows: true);

            await Run(h);

            h.Sent.Should().BeEmpty();
            h.StoredError().Should().Be("timeout");
        }
    }
}
