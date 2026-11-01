using FluentAssertions;
using Blocks.Genesis;
using Moq;
using MongoDB.Bson;
using MongoDB.Driver;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Utils;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// blocks-logic#380, against a real Mongo (localhost:27017, or BLOCKS_FUNCTIONS_TEST_MONGO): a started error run
    /// is found again by its target and failed attempt, so a redelivered event starts nothing.
    /// </summary>
    public sealed class ErrorTriggerRepositoryTests : IDisposable
    {
        private const string Tenant = "tenant-wf-error-trigger";

        private readonly MongoClient _client;
        private readonly string _databaseName = "blocks_wf_error_trigger_" + Guid.NewGuid().ToString("N");
        private readonly WorkflowExecutionRepository _repository;

        public ErrorTriggerRepositoryTests()
        {
            var url = Environment.GetEnvironmentVariable("BLOCKS_FUNCTIONS_TEST_MONGO") ?? "mongodb://localhost:27017";
            _client = new MongoClient(url);
            var collection = _client.GetDatabase(_databaseName).GetCollection<WorkflowExecutionEntity>("WorkflowExecutions");

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetCollection<WorkflowExecutionEntity>(It.IsAny<string>(), It.IsAny<string>())).Returns(collection);
            _repository = new WorkflowExecutionRepository(provider.Object, new XUnitTest.TestDoubles.PassThroughLogicDataAccess());
        }

        public void Dispose() => _client.DropDatabase(_databaseName);

        private static NodeExecutionEntity Row(string nodeId) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            NodeId = nodeId,
            NodeName = nodeId,
            NodeType = "action",
            NodeVersion = "v1",
            Status = NodeExecutionStatus.Running,
            StartedAt = DateTime.UtcNow,
        };

        private async Task<WorkflowExecutionEntity> CreateAsync(int branches, TriggerMetadata trigger, string workflowId)
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = Tenant,
                WorkflowId = workflowId,
                WorkflowName = "wf",
                WorkflowSnapshot = new WorkflowEntity { TenantId = Tenant },
                TriggerMetadata = trigger ?? new TriggerMetadata(),
                ExecutionMode = WorkflowExecutionMode.Production,
                Status = WorkflowExecutionStatus.Running,
                NodeExecutions = [.. Enumerable.Range(0, branches).Select(i => Row($"n{i}"))],
                StartedAt = DateTime.UtcNow,
            };
            await _repository.CreateAsync(execution);
            return execution;
        }

        [Fact]
        public async Task C6_AnErrorRun_IsFoundByItsTargetFailedExecutionAndAttempt_Only()
        {
            var payload = new BsonDocument
            {
                { "execution", new BsonDocument { { "id", "exec-a" }, { "attemptNumber", 2 } } },
                { "workflow", new BsonDocument { { "id", "wf-a" }, { "name", "Sync orders" } } },
            };
            await CreateAsync(1, new TriggerMetadata
            {
                TriggerNodeId = "err",
                TriggerType = LogicConstants.ErrorTriggerNodeType,
                TriggerData = [payload],
            }, workflowId: "wf-b");

            (await _repository.ErrorRunExistsAsync(Tenant, "wf-b", "exec-a", 2)).Should().BeTrue();
            (await _repository.ErrorRunExistsAsync(Tenant, "wf-b", "exec-a", 1)).Should().BeFalse();
            (await _repository.ErrorRunExistsAsync(Tenant, "wf-b", "exec-other", 2)).Should().BeFalse();
            (await _repository.ErrorRunExistsAsync(Tenant, "wf-c", "exec-a", 2)).Should().BeFalse();
        }
    }
}
