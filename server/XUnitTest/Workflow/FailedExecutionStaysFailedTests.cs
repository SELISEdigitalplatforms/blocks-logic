using FluentAssertions;
using Blocks.Genesis;
using Moq;
using MongoDB.Driver;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Repositories;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// 2026-10-08: a node failed and the execution still said "Completed". Branch B read the execution
    /// before branch A failed it, then its node start set Running again, and the completion step later
    /// wrote Completed. Against a real Mongo (localhost:27017, or BLOCKS_FUNCTIONS_TEST_MONGO).
    /// </summary>
    public sealed class FailedExecutionStaysFailedTests : IDisposable
    {
        private const string Tenant = "tenant-wf-failed";

        private readonly MongoClient _client;
        private readonly string _databaseName = "blocks_wf_failed_" + Guid.NewGuid().ToString("N");
        private readonly WorkflowExecutionRepository _repository;

        public FailedExecutionStaysFailedTests()
        {
            var url = Environment.GetEnvironmentVariable("BLOCKS_FUNCTIONS_TEST_MONGO") ?? "mongodb://localhost:27017";
            _client = new MongoClient(url);
            var collection = _client.GetDatabase(_databaseName).GetCollection<WorkflowExecutionEntity>("WorkflowExecutions");

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetCollection<WorkflowExecutionEntity>(It.IsAny<string>(), It.IsAny<string>())).Returns(collection);
            _repository = new WorkflowExecutionRepository(provider.Object);
        }

        public void Dispose() => _client.DropDatabase(_databaseName);

        private static NodeExecutionEntity Row(string nodeId, NodeExecutionStatus status = NodeExecutionStatus.Running) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            NodeId = nodeId,
            NodeName = nodeId,
            NodeType = "action",
            NodeVersion = "v1",
            Status = status,
            StartedAt = DateTime.UtcNow,
        };

        /// <summary>Two branches running, "a" and "b"; "a" then fails.</summary>
        private async Task<(string Id, NodeExecutionEntity A)> TwoBranchesWithAFailedAsync(WorkflowExecutionMode mode)
        {
            var a = Row("a");
            var execution = new WorkflowExecutionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = Tenant,
                WorkflowId = "wf-1",
                WorkflowName = "wf",
                WorkflowSnapshot = new WorkflowEntity { TenantId = Tenant },
                TriggerMetadata = new TriggerMetadata(),
                ExecutionMode = mode,
                Status = WorkflowExecutionStatus.Running,
                ActiveNodeIds = ["a", "b"],
                NodeExecutions = [a, Row("b")],
                StartedAt = DateTime.UtcNow,
            };
            await _repository.CreateAsync(execution);
            await _repository.AtomicUpdateNodeExecutionFailedAsync(execution.Id!, Tenant, a.Id, "boom", 0, []);
            await _repository.AtomicCompleteNodeAsync(execution.Id!, Tenant, "a", []);
            return (execution.Id!, a);
        }

        private async Task<WorkflowExecutionStatus> StatusAsync(string id) => (await _repository.GetByIdAsync(id, Tenant))!.Status;

        [Theory]
        [InlineData(WorkflowExecutionMode.Test)]
        [InlineData(WorkflowExecutionMode.Production)]
        public async Task The_other_branch_cannot_start_a_node_or_complete_the_run_after_a_failure(WorkflowExecutionMode mode)
        {
            var (id, _) = await TwoBranchesWithAFailedAsync(mode);

            // Branch b finishes and hands on to "c"; b's next node may not start, nor set Running again.
            (await _repository.AtomicCompleteNodeAsync(id, Tenant, "b", ["c"])).Should().BeFalse();
            (await _repository.TryAddNodeExecutionAsync(id, Tenant, Row("c"))).Should().BeFalse();
            (await _repository.TryAddFirstNodeExecutionAsync(id, Tenant, Row("c"))).Should().BeFalse();
            (await _repository.AtomicCompleteNodeAsync(id, Tenant, "c", [])).Should().BeFalse();

            (await StatusAsync(id)).Should().Be(WorkflowExecutionStatus.Failed);
            (await _repository.GetByIdAsync(id, Tenant))!.NodeExecutions.Should().NotContain(r => r.NodeId == "c");
        }

        [Fact]
        public async Task A_step_target_reached_after_another_branch_failed_does_not_finalize_as_completed()
        {
            var (id, _) = await TwoBranchesWithAFailedAsync(WorkflowExecutionMode.Test);

            (await _repository.AtomicFinalizeExecutionAsync(id, Tenant)).Should().BeFalse();
            (await StatusAsync(id)).Should().Be(WorkflowExecutionStatus.Failed);
        }

        [Fact]
        public async Task A_run_that_did_not_fail_still_starts_nodes_and_completes()
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = Tenant,
                WorkflowId = "wf-1",
                WorkflowName = "wf",
                WorkflowSnapshot = new WorkflowEntity { TenantId = Tenant },
                TriggerMetadata = new TriggerMetadata(),
                ExecutionMode = WorkflowExecutionMode.Test,
                Status = WorkflowExecutionStatus.Queued,
                ActiveNodeIds = ["a"],
                StartedAt = DateTime.UtcNow,
            };
            await _repository.CreateAsync(execution);

            (await _repository.TryAddNodeExecutionAsync(execution.Id!, Tenant, Row("a"))).Should().BeTrue();
            (await StatusAsync(execution.Id!)).Should().Be(WorkflowExecutionStatus.Running);
            (await _repository.AtomicCompleteNodeAsync(execution.Id!, Tenant, "a", [])).Should().BeTrue();
            (await StatusAsync(execution.Id!)).Should().Be(WorkflowExecutionStatus.Completed);
            (await _repository.AtomicFinalizeExecutionAsync(execution.Id!, Tenant)).Should().BeTrue("finalizing a completed run is still allowed");
        }

        [Fact]
        public async Task A_resumed_failed_run_starts_nodes_again()
        {
            var (id, _) = await TwoBranchesWithAFailedAsync(WorkflowExecutionMode.Production);

            (await _repository.TryReopenFailedExecutionAsync(id, Tenant)).Should().BeTrue();
            (await _repository.TryAddFirstNodeExecutionAsync(id, Tenant, Row("a"))).Should().BeTrue("a Failed row does not block a retry");
            (await StatusAsync(id)).Should().Be(WorkflowExecutionStatus.Running);
        }
    }
}
