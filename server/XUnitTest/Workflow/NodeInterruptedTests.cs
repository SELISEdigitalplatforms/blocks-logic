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
    /// A step still working when its Worker shuts down stops and is marked interrupted — not left Running
    /// for ever (the repeated message is ignored by the run-once guard) and not called a plain failure.
    /// </summary>
    public class NodeInterruptedTests
    {
        private sealed class WaitsUntilStopped : INodeExecutor
        {
            public string NodeType => "slow";
            public string Version => "v1";
            public async Task<NodeExecutionResult> RunAsync(NodeExecutionContext context)
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
                return NodeExecutionResult.Empty();
            }
        }

        [Fact]
        public async Task A_step_working_at_shutdown_ends_as_interrupted()
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1",
                TenantId = "t1",
                WorkflowId = "wf",
                WorkflowName = "wf",
                ExecutionMode = WorkflowExecutionMode.Production,
                TriggerMetadata = new TriggerMetadata(),
                WorkflowSnapshot = new WorkflowEntity
                {
                    TenantId = "t1",
                    Nodes = [new NodeEntity { Id = "n1", Name = "Slow", Category = "action", Type = "slow", Version = "v1", Position = new Position() }],
                    Edges = [],
                },
            };
            var repository = new Mock<IWorkflowExecutionRepository> { DefaultValue = DefaultValue.Empty };
            repository.Setup(r => r.GetByIdAsync("exec-1", "t1")).ReturnsAsync(execution);
            repository.Setup(r => r.GetItemsByNodeIdsAsync(It.IsAny<string>(), It.IsAny<List<Dictionary<string, string>>>(), It.IsAny<string>()))
                .ReturnsAsync(new List<WorkflowItemExecutionEntity>());
            repository.Setup(r => r.TryAddFirstNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            string? error = null;
            repository.Setup(r => r.AtomicUpdateNodeExecutionFailedAsync("exec-1", "t1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Dictionary<string, int>>()))
                .Callback((string _, string _, string _, string e, int _, Dictionary<string, int> _) => error = e)
                .Returns(Task.CompletedTask);

            var engine = new WorkflowEngineService(repository.Object, [new WaitsUntilStopped()], Mock.Of<IMessageClient>(),
                NullLogger<WorkflowEngineService>.Instance, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IServiceProvider>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance));

            using var stopping = new CancellationTokenSource();
            var run = engine.RunNodeAsync(new AddExcuationNodeEvent { TenantId = "t1", WorkflowId = "wf", WorkflowExecutionId = "exec-1", NodeId = "n1" }, stopping.Token);
            await Task.Delay(100);
            stopping.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            error.Should().Contain("Interrupted").And.Contain("may already have done its work");
            execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
        }
    }
}
