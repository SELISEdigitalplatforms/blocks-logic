using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Blocks.Genesis;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// WF-40: pins apply in every Test-mode run that goes through the normal engine (a step test that
    /// waits for its webhook, email or mock-data event), not only in the step-test loop. Production
    /// never uses them. A failed node's stored error is safe to show the user.
    /// </summary>
    public class PinnedTestRunTests
    {
        private sealed class CountingExecutor(Func<NodeExecutionResult> run) : INodeExecutor
        {
            public int Calls;
            public string NodeType => "fn";
            public string Version => "v1";
            public Task<NodeExecutionResult> RunAsync(NodeExecutionContext context)
            {
                Calls++;
                return Task.FromResult(run());
            }
        }

        private sealed record Harness(
            WorkflowEngineService Engine,
            WorkflowExecutionEntity Execution,
            List<WorkflowItemExecutionEntity> Items,
            Func<string?> StoredError);

        private static Harness Build(WorkflowExecutionMode mode, string category, BsonArray? pin, INodeExecutor executor)
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1",
                TenantId = "t1",
                WorkflowId = "wf",
                WorkflowName = "wf",
                ExecutionMode = mode,
                TriggerMetadata = new TriggerMetadata(),
                WorkflowSnapshot = new WorkflowEntity
                {
                    TenantId = "t1",
                    Nodes = [new NodeEntity { Id = "n1", Name = "Charge", Category = category, Type = "fn", Version = "v1", Position = new Position(), PinData = pin }],
                    Edges = [],
                    TestMeta = new TestWorkflowMeta(),
                },
            };
            var items = new List<WorkflowItemExecutionEntity>();
            string? error = null;
            var repository = new Mock<IWorkflowExecutionRepository> { DefaultValue = DefaultValue.Empty };
            repository.Setup(r => r.GetByIdAsync("exec-1", "t1")).ReturnsAsync(execution);
            repository.Setup(r => r.GetItemsByNodeIdsAsync(It.IsAny<string>(), It.IsAny<List<Dictionary<string, string>>>(), It.IsAny<string>()))
                .ReturnsAsync(new List<WorkflowItemExecutionEntity>());
            repository.Setup(r => r.TryAddFirstNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            repository.Setup(r => r.TryAddNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            repository.Setup(r => r.AddItemsAsync(It.IsAny<string>(), It.IsAny<List<WorkflowItemExecutionEntity>>()))
                .Callback((string _, List<WorkflowItemExecutionEntity> added) => items.AddRange(added))
                .Returns(Task.CompletedTask);
            repository.Setup(r => r.AtomicUpdateNodeExecutionFailedAsync("exec-1", "t1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Dictionary<string, int>>()))
                .Callback((string _, string _, string _, string e, int _, Dictionary<string, int> _) => error = e)
                .Returns(Task.CompletedTask);

            var engine = new WorkflowEngineService(repository.Object, [executor], Mock.Of<IMessageClient>(),
                NullLogger<WorkflowEngineService>.Instance, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IServiceProvider>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance));
            return new Harness(engine, execution, items, () => error);
        }

        private static AddExcuationNodeEvent Event() =>
            new() { TenantId = "t1", WorkflowId = "wf", WorkflowExecutionId = "exec-1", NodeId = "n1" };

        private static readonly BsonArray Pin = new() { new BsonDocument("charged", true) };

        [Fact]
        public async Task Webhook_test_run_does_not_call_a_pinned_action_node()
        {
            var executor = new CountingExecutor(() => NodeExecutionResult.Successful([]));
            var h = Build(WorkflowExecutionMode.Test, "action", Pin, executor);

            await h.Engine.RunNodeInProcessAsync(Event());

            executor.Calls.Should().Be(0);
            h.StoredError().Should().BeNull();
            h.Items.Should().ContainSingle().Which.Data.Output["charged"].AsBoolean.Should().BeTrue();
        }

        [Fact]
        public async Task Queued_test_run_on_the_worker_does_not_call_a_pinned_action_node()
        {
            var executor = new CountingExecutor(() => NodeExecutionResult.Successful([]));
            var h = Build(WorkflowExecutionMode.Test, "action", Pin, executor);

            await h.Engine.RunNodeAsync(Event());

            executor.Calls.Should().Be(0);
            h.Items.Should().ContainSingle();
        }

        [Fact]
        public async Task Production_run_ignores_pins_and_calls_the_node()
        {
            var executor = new CountingExecutor(() => NodeExecutionResult.Successful([]));
            var h = Build(WorkflowExecutionMode.Production, "action", Pin, executor);

            await h.Engine.RunNodeAsync(Event());

            executor.Calls.Should().Be(1);
            h.Items.Should().BeEmpty();
        }

        [Fact]
        public async Task Pinned_logic_node_still_runs_and_its_failure_stays_a_failure()
        {
            var executor = new CountingExecutor(() => NodeExecutionResult.Failed("Field 'amount' is missing."));
            var h = Build(WorkflowExecutionMode.Test, "logic", Pin, executor);

            await h.Engine.RunNodeInProcessAsync(Event());

            executor.Calls.Should().Be(1);
            h.StoredError().Should().Be("Field 'amount' is missing.");
            h.Execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        }

        [Fact]
        public async Task Unexpected_exception_is_stored_without_its_message_or_stack()
        {
            var executor = new CountingExecutor(() => throw new InvalidOperationException("mongodb://10.10.64.9:27017 timed out"));
            var h = Build(WorkflowExecutionMode.Test, "action", pin: null, executor);

            await h.Engine.RunNodeInProcessAsync(Event());

            h.StoredError().Should().Be(WorkflowEngineService.UnexpectedErrorMessage);
            h.Execution.ErrorMessage.Should().Be(WorkflowEngineService.UnexpectedErrorMessage);
            h.Execution.NodeExecutions.Should().OnlyContain(ne => ne.Error == null || !ne.Error.Contains("10.10.64.9"));
        }

        [Theory]
        [InlineData("NodeReportedFailure", "Bad input.", "Bad input.")]
        [InlineData("NodeReportedFailure", null, "The step failed.")]
        [InlineData("NodeReportedFailure", "", "The step failed.")]
        [InlineData("Interrupted", WorkflowEngineService.InterruptedMessage, WorkflowEngineService.InterruptedMessage)]
        [InlineData("InvalidOperationException", "internal detail", WorkflowEngineService.UnexpectedErrorMessage)]
        public void UserFacingError_keeps_only_engine_written_messages(string kind, string? message, string expected)
        {
            WorkflowEngineService.UserFacingError(new Exception(message), kind).Should().Be(expected);
        }
    }
}
