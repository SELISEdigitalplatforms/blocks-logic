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
    /// blocks-logic#380: a failed production attempt, or a failed full test run, sends one ErrorTriggerEvent
    /// when the run's workflow has an enabled alert, and nothing in every other case.
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
            bool sendThrows = false,
            string? completionNodeId = null,
            bool hasTestMeta = true)
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
                    TestMeta = hasTestMeta ? new TestWorkflowMeta { UserIds = ["user-1"], CompletionNodeId = completionNodeId } : null,
                    Settings = settings,
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

            var sent = new List<ErrorTriggerEvent>();
            var messages = new Mock<IMessageClient>();
            if (sendThrows)
                messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<ErrorTriggerEvent>>())).ThrowsAsync(new TimeoutException("broker"));
            else
                messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<ErrorTriggerEvent>>()))
                    .Callback((ConsumerMessage<ErrorTriggerEvent> m) =>
                    {
                        m.ConsumerName.Should().Be(LogicConstants.ErrorTriggerQueue);
                        sent.Add(m.Payload);
                    })
                    .Returns(Task.CompletedTask);

            var engine = new WorkflowEngineService(repository.Object, [new FailingExecutor()], messages.Object,
                NullLogger<WorkflowEngineService>.Instance, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IServiceProvider>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance));
            return new Harness(engine, sent, () => error);
        }

        private static Task Run(Harness h) =>
            h.Engine.RunNodeAsync(new AddExcuationNodeEvent { TenantId = "t1", WorkflowId = "wf-a", WorkflowExecutionId = "exec-1", NodeId = "n1" });

        [Fact]
        public async Task H8_AProductionFailureWithAnEnabledAlert_SendsOneEvent()
        {
            var h = Build(settings: EnabledAlert);

            await Run(h);

            h.Sent.Should().ContainSingle();
            h.Sent[0].Should().BeEquivalentTo(new ErrorTriggerEvent
            {
                TenantId = "t1",
                FailedWorkflowId = "wf-a",
                FailedExecutionId = "exec-1",
                Error = new ErrorTriggerEventError { Message = "timeout", NodeId = "n1", NodeName = "Postgres", NodeType = "fn" },
            });
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
        public async Task C1_NoUsableAlert_SendsNothing(WorkflowSettings? settings)
        {
            var h = Build(settings: settings);

            await Run(h);

            h.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task C2_AFullTestRun_SendsOneEvent()
        {
            var h = Build(mode: WorkflowExecutionMode.Test, settings: EnabledAlert);

            await Run(h);

            h.Sent.Should().ContainSingle();
            h.Sent[0].FailedExecutionId.Should().Be("exec-1");
        }

        [Fact]
        public async Task C2a_ATestRunWithoutTestSettings_RunsAndAlerts()
        {
            // An error workflow's test run uses its saved draft, which has no TestMeta unless someone is listening.
            var h = Build(mode: WorkflowExecutionMode.Test, settings: EnabledAlert, hasTestMeta: false);

            await Run(h);

            h.StoredError().Should().Be("timeout");
            h.Sent.Should().ContainSingle();
        }

        [Fact]
        public async Task C2b_AStepTest_SendsNothing()
        {
            var h = Build(mode: WorkflowExecutionMode.Test, settings: EnabledAlert, completionNodeId: "n1");

            await Run(h);

            h.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task C3_ARunStartedByAnErrorTrigger_NeverAlerts()
        {
            var h = Build(settings: EnabledAlert, triggerType: LogicConstants.ErrorTriggerNodeType);

            await Run(h);

            h.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task C4_ARunAnotherBranchAlreadyFailed_DoesNotAlertAgain()
        {
            var h = Build(settings: EnabledAlert, statusBefore: WorkflowExecutionStatus.Failed);

            await Run(h);

            h.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task C5_ASendFailure_IsSwallowed_AndTheRunStillFails()
        {
            var h = Build(settings: EnabledAlert, sendThrows: true);

            await Run(h);

            h.Sent.Should().BeEmpty();
            h.StoredError().Should().Be("timeout");
        }
    }
}
