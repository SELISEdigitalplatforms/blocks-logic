using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.TriggerErrorTriggerV2;
using Workflow.DomainService.Nodes.TriggerWebhookV1;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// blocks-logic#380 end to end in one process: the real engine, execution service and repositories against a real
    /// Mongo (localhost:27017, or BLOCKS_FUNCTIONS_TEST_MONGO). Only the message bus is a list the test drains,
    /// one message at a time, the way a Worker lane does.
    /// </summary>
    public sealed class ErrorTriggerFlowTests : IDisposable
    {
        private const string Tenant = "tenant-error-flow";

        private sealed class FailingExecutor : INodeExecutor
        {
            public string NodeType => "fail";
            public string Version => "v1";
            public Task<NodeExecutionResult> RunAsync(NodeExecutionContext context) =>
                Task.FromResult(NodeExecutionResult.Failed("Order API returned 500"));
        }

        private readonly MongoClient _client;
        private readonly string _databaseName = "blocks_wf_error_flow_" + Guid.NewGuid().ToString("N");
        private readonly WorkflowRepository _workflows;
        private readonly WorkflowVersionRepository _versions;
        private readonly WorkflowExecutionRepository _executions;
        private readonly WorkflowEngineService _engine;
        private readonly WorkflowExecutionService _executionService;
        private readonly Queue<object> _bus = new();
        private readonly List<ErrorTriggerEvent> _alerts = [];

        public ErrorTriggerFlowTests()
        {
            TestBlocksContext.Set(Tenant, "user-1");
            var url = Environment.GetEnvironmentVariable("BLOCKS_FUNCTIONS_TEST_MONGO") ?? "mongodb://localhost:27017";
            _client = new MongoClient(url);
            var database = _client.GetDatabase(_databaseName);

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetCollection<WorkflowEntity>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string name) => database.GetCollection<WorkflowEntity>(name));
            provider.Setup(p => p.GetCollection<WorkflowVersionEntity>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string name) => database.GetCollection<WorkflowVersionEntity>(name));
            provider.Setup(p => p.GetCollection<WorkflowExecutionEntity>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string name) => database.GetCollection<WorkflowExecutionEntity>(name));
            provider.Setup(p => p.GetCollection<WorkflowItemExecutionEntity>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string name) => database.GetCollection<WorkflowItemExecutionEntity>(name));

            var access = new XUnitTest.TestDoubles.PassThroughLogicDataAccess();
            _workflows = new WorkflowRepository(provider.Object, access);
            _versions = new WorkflowVersionRepository(provider.Object, access);
            _executions = new WorkflowExecutionRepository(provider.Object, access);

            var messages = new Mock<IMessageClient>();
            messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()))
                .Callback((ConsumerMessage<AddExcuationNodeEvent> m) => _bus.Enqueue(m.Payload))
                .Returns(Task.CompletedTask);
            messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<ErrorTriggerEvent>>()))
                .Callback((ConsumerMessage<ErrorTriggerEvent> m) =>
                {
                    m.ConsumerName.Should().Be(LogicConstants.ErrorTriggerQueue);
                    _alerts.Add(m.Payload);
                    _bus.Enqueue(m.Payload);
                })
                .Returns(Task.CompletedTask);

            var logger = new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance);
            _engine = new WorkflowEngineService(_executions,
                [new TriggerWebhookV1Node(), new TriggerErrorTriggerV2Node(), new FailingExecutor()],
                messages.Object, NullLogger<WorkflowEngineService>.Instance, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IServiceProvider>(), logger, _workflows);
            _executionService = new WorkflowExecutionService(_workflows, _executions, messages.Object,
                NullLogger<WorkflowExecutionService>.Instance, _engine, _versions, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IWorkflowAuthService>(), Mock.Of<IHttpContextAccessor>(), Mock.Of<IDelegationGrantFactory>(),
                logger, Mock.Of<IInProcessGrantReleaser>());
        }

        public void Dispose()
        {
            _client.DropDatabase(_databaseName);
            TestBlocksContext.Clear();
        }

        private static NodeEntity Node(string id, string name, string category, string type, string version) =>
            new() { Id = id, Name = name, Category = category, Type = type, Version = version, Position = new Position() };

        private static EdgeEnity Edge(string source, string target) =>
            new() { Id = $"{source}-{target}", Source = source, Target = target, SourceHandle = "source", TargetHandle = "target" };

        /// <summary>A published workflow: its live document, its version and the published trigger nodes.</summary>
        private async Task PublishAsync(string id, string name, List<NodeEntity> nodes, List<EdgeEnity> edges, WorkflowSettings? settings = null)
        {
            var workflow = new WorkflowEntity
            {
                ItemId = id, Name = name, TenantId = Tenant, Nodes = nodes, Edges = edges,
                Settings = settings ?? new WorkflowSettings(),
                IsPublished = true, IsDirty = false, PublishedVersionId = $"{id}-v1",
                PublishedMeta = new PublishedWorkflowMeta { TriggerNodes = [.. nodes.Where(n => n.Category == LogicConstants.TriggerCategory)] },
            };
            await _workflows.CreateWorkflowAsync(workflow);
            await _versions.CreateWorkflowVersionAsync(new WorkflowVersionEntity
            {
                ItemId = $"{id}-v1", TenantId = Tenant, WorkflowId = id, Name = "v1", Snapshot = workflow,
            });
        }

        /// <summary>A: webhook → one or more steps that fail. B: Error Trigger only.</summary>
        private async Task SeedAsync(WorkflowSettings? alertSettings, int failingBranches = 1)
        {
            var failing = Enumerable.Range(1, failingBranches).Select(i => Node($"fail{i}", $"Call API {i}", "action", "fail", "v1")).ToList();
            await PublishAsync("wf-a", "Sync orders",
                [Node("hook", "Webhook", "trigger", "webhook", "v2"), .. failing],
                [.. failing.Select(f => Edge("hook", f.Id))],
                alertSettings);
            await PublishAsync("wf-b", "Ops alerts", [Node("err", "Error Trigger", "trigger", LogicConstants.ErrorTriggerNodeType, "v2")], []);
        }

        private static WorkflowSettings AlertTo(string workflowId, bool isEnabled = true) =>
            new() { Alert = new WorkflowAlert { IsEnabled = isEnabled, Mode = WorkflowAlertMode.Workflow, WorkflowId = workflowId } };

        /// <summary>Starts a run of A the way a trigger does, and drains the bus.</summary>
        private async Task<string> RunAAsync(WorkflowExecutionMode mode = WorkflowExecutionMode.Production)
        {
            var version = await _versions.GetWorkflowVersionAsync(Tenant, "wf-a-v1");
            // A real test run always carries its test settings on the snapshot.
            version.Snapshot.TestMeta ??= new TestWorkflowMeta();
            var input = new BsonArray { new BsonDocument("orderId", 42) };
            var execution = new WorkflowExecutionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = Tenant,
                WorkflowId = "wf-a",
                WorkflowName = "Sync orders",
                WorkflowSnapshot = version.Snapshot,
                ExecutionMode = mode,
                TriggerMetadata = new TriggerMetadata { TriggerNodeId = "hook", TriggerType = "webhook", TriggerData = input },
                Status = WorkflowExecutionStatus.Queued,
                ActiveNodeIds = ["hook"],
                StartedAt = DateTime.UtcNow,
            };
            execution.Context[LogicConstants.ExecutionInputKey] = input;
            await _executions.CreateAsync(execution);
            _bus.Enqueue(new AddExcuationNodeEvent { TenantId = Tenant, WorkflowId = "wf-a", WorkflowExecutionId = execution.Id!, NodeId = "hook" });
            await DrainAsync();
            return execution.Id!;
        }

        private async Task DrainAsync()
        {
            while (_bus.TryDequeue(out var message))
            {
                if (message is AddExcuationNodeEvent node) await _engine.RunNodeAsync(node);
                else if (message is ErrorTriggerEvent alert) await _executionService.ErrorTriggerStartAsync(alert);
            }
        }

        private async Task<List<WorkflowExecutionEntity>> RunsOfBAsync() =>
            await _client.GetDatabase(_databaseName).GetCollection<WorkflowExecutionEntity>("WorkflowExecutions")
                .Find(e => e.WorkflowId == "wf-b").SortBy(e => e.StartedAt).ToListAsync();

        private async Task<BsonDocument> ErrorTriggerOutputAsync(string executionId)
        {
            var items = await _client.GetDatabase(_databaseName).GetCollection<WorkflowItemExecutionEntity>("WorkflowItemExecutions")
                .Find(i => i.WorkflowExecutionId == executionId && i.NodeId == "err").ToListAsync();
            items.Should().ContainSingle();
            return items[0].Data.Output.AsBsonDocument;
        }

        [Fact]
        public async Task A_failing_production_run_starts_the_error_workflow_once_with_the_failure_details()
        {
            await SeedAsync(AlertTo("wf-b"));

            var failedId = await RunAAsync();

            (await _executions.GetByIdAsync(failedId, Tenant))!.Status.Should().Be(WorkflowExecutionStatus.Failed);
            _alerts.Should().ContainSingle();
            var runs = await RunsOfBAsync();
            runs.Should().ContainSingle();
            var run = runs[0];
            run.ExecutionMode.Should().Be(WorkflowExecutionMode.Production);
            run.Status.Should().Be(WorkflowExecutionStatus.Completed);
            run.TriggerMetadata!.TriggerType.Should().Be(LogicConstants.ErrorTriggerNodeType);

            var output = await ErrorTriggerOutputAsync(run.Id!);
            output["execution"]["id"].AsString.Should().Be(failedId);
            output["execution"]["attemptNumber"].AsInt32.Should().Be(1);
            output["execution"]["mode"].AsString.Should().Be("Production");
            output["execution"]["error"]["message"].AsString.Should().Be("Order API returned 500");
            output["execution"]["error"]["nodeId"].AsString.Should().Be("fail1");
            output["execution"]["error"]["nodeName"].AsString.Should().Be("Call API 1");
            output["execution"]["error"]["nodeType"].AsString.Should().Be("fail");
            output["workflow"].AsBsonDocument.Should().BeEquivalentTo(new BsonDocument { { "id", "wf-a" }, { "name", "Sync orders" } });
        }

        [Fact]
        public async Task Two_failing_branches_alert_once()
        {
            await SeedAsync(AlertTo("wf-b"), failingBranches: 2);

            await RunAAsync();

            _alerts.Should().ContainSingle();
            (await RunsOfBAsync()).Should().ContainSingle();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(null)]
        public async Task No_alert_or_a_paused_alert_sends_nothing(bool? isEnabled)
        {
            await SeedAsync(isEnabled is null ? null : AlertTo("wf-b", isEnabled.Value));

            var failedId = await RunAAsync();

            (await _executions.GetByIdAsync(failedId, Tenant))!.Status.Should().Be(WorkflowExecutionStatus.Failed);
            _alerts.Should().BeEmpty();
            (await RunsOfBAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task A_failing_test_run_sends_nothing()
        {
            await SeedAsync(AlertTo("wf-b"));

            await RunAAsync(WorkflowExecutionMode.Test);

            _alerts.Should().BeEmpty();
            (await RunsOfBAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task A_resumed_run_that_fails_again_alerts_again_for_the_next_attempt()
        {
            // Resume continues the steps still pending when the run failed: branch 2 never started after branch 1 failed it.
            await SeedAsync(AlertTo("wf-b"), failingBranches: 2);
            var failedId = await RunAAsync();

            var resumed = await _executionService.ResumeExecutionAsync(Tenant, new WorkflowExecutionGetRequestDto { ExecutionId = failedId });
            resumed.IsSuccess.Should().BeTrue(resumed.Error);
            await DrainAsync();

            _alerts.Select(a => a.AttemptNumber).Should().Equal(1, 2);
            var runs = await RunsOfBAsync();
            runs.Should().HaveCount(2);
            (await ErrorTriggerOutputAsync(runs[1].Id!))["execution"]["attemptNumber"].AsInt32.Should().Be(2);
        }

        [Fact]
        public async Task A_redelivered_alert_starts_the_error_workflow_only_once()
        {
            await SeedAsync(AlertTo("wf-b"));
            await RunAAsync();

            await _executionService.ErrorTriggerStartAsync(_alerts[0]);
            await DrainAsync();

            (await RunsOfBAsync()).Should().ContainSingle();
        }

        [Fact]
        public async Task An_error_workflow_that_fails_never_alerts_again()
        {
            // B handles A's failures and itself fails; B's own alert points back at A.
            await PublishAsync("wf-a", "Sync orders",
                [Node("hook", "Webhook", "trigger", "webhook", "v2"), Node("fail1", "Call API 1", "action", "fail", "v1")],
                [Edge("hook", "fail1")], AlertTo("wf-b"));
            await PublishAsync("wf-b", "Ops alerts",
                [Node("err", "Error Trigger", "trigger", LogicConstants.ErrorTriggerNodeType, "v2"), Node("fail1", "Notify", "action", "fail", "v1")],
                [Edge("err", "fail1")], AlertTo("wf-a"));

            await RunAAsync();

            _alerts.Should().ContainSingle();
            var runs = await RunsOfBAsync();
            runs.Should().ContainSingle();
            runs[0].Status.Should().Be(WorkflowExecutionStatus.Failed);
        }

        [Fact]
        public async Task An_alert_turned_on_after_publishing_takes_effect_without_republishing()
        {
            await SeedAsync(alertSettings: null);
            (await _workflows.UpdateWorkflowSettingsAsync(Tenant, "wf-a", AlertTo("wf-b"), "user-1")).Should().BeTrue();

            await RunAAsync();

            (await RunsOfBAsync()).Should().ContainSingle();
        }

        [Fact]
        public async Task Saving_settings_writes_only_settings_and_marks_the_workflow_dirty()
        {
            await SeedAsync(alertSettings: null);
            var before = await _workflows.GetWorkflowAsync(Tenant, "wf-a");

            await _workflows.UpdateWorkflowSettingsAsync(Tenant, "wf-a", AlertTo("wf-b"), "user-1");

            var after = await _workflows.GetWorkflowAsync(Tenant, "wf-a");
            after.Settings!.Alert.Should().BeEquivalentTo(new WorkflowAlert { IsEnabled = true, Mode = WorkflowAlertMode.Workflow, WorkflowId = "wf-b" });
            after.IsDirty.Should().BeTrue();
            after.LastUpdatedBy.Should().Be("user-1");
            after.Nodes.Select(n => n.Id).Should().Equal(before.Nodes.Select(n => n.Id));
            after.IsPublished.Should().BeTrue();
            after.PublishedVersionId.Should().Be(before.PublishedVersionId);
            (await _workflows.GetWorkflowSettingsAsync(Tenant, "wf-a"))!.Alert!.WorkflowId.Should().Be("wf-b");
        }

        [Fact]
        public async Task The_picker_lists_only_published_workflows_with_an_Error_Trigger_and_leaves_one_out()
        {
            await SeedAsync(alertSettings: null);
            await PublishAsync("wf-c", "Billing alerts", [Node("err", "Error Trigger", "trigger", LogicConstants.ErrorTriggerNodeType, "v2")], []);
            // A draft Error Trigger that is not published does not count.
            await _workflows.CreateWorkflowAsync(new WorkflowEntity
            {
                ItemId = "wf-d", Name = "Draft alerts", TenantId = Tenant, IsPublished = false,
                Nodes = [Node("err", "Error Trigger", "trigger", LogicConstants.ErrorTriggerNodeType, "v2")],
            });

            var all = await _workflows.GetAllWorkflowsAsync(Tenant, 50, 0, null, null, hasErrorTrigger: true, excludeItemId: null);
            all.Select(w => w.ItemId).Should().BeEquivalentTo("wf-b", "wf-c");
            (await _workflows.GetWorkflowsCountAsync(Tenant, null, null, true, null)).Should().Be(2);

            var withoutB = await _workflows.GetAllWorkflowsAsync(Tenant, 50, 0, "alerts", null, true, excludeItemId: "wf-b");
            withoutB.Select(w => w.ItemId).Should().Equal("wf-c");

            (await _workflows.GetWorkflowsCountAsync(Tenant, null, null, null, null)).Should().Be(4);
        }
    
        // ---- Old data written before typed settings (blocks-logic#380 must not change old workflows) ----

        public static TheoryData<string> LegacySettings => new() { "missing", "null", "{}", "{ \"timezone\": \"UTC\" }" };

        private static BsonDocument WithLegacySettings(BsonDocument document, string shape)
        {
            document.Remove("Settings");
            if (shape == "null") document["Settings"] = BsonNull.Value;
            else if (shape != "missing") document["Settings"] = BsonDocument.Parse(shape);
            return document;
        }

        /// <summary>Rewrites stored documents (live workflow, version snapshot, execution snapshot) to an old Settings shape.</summary>
        private async Task MakeLegacyAsync(string shape, string? executionId = null)
        {
            var database = _client.GetDatabase(_databaseName);
            var workflows = database.GetCollection<BsonDocument>("Workflows");
            foreach (var doc in await workflows.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
                await workflows.ReplaceOneAsync(new BsonDocument("_id", doc["_id"]), WithLegacySettings(doc, shape));

            var versions = database.GetCollection<BsonDocument>("WorkflowVersions");
            foreach (var doc in await versions.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
            {
                WithLegacySettings(doc["Snapshot"].AsBsonDocument, shape);
                await versions.ReplaceOneAsync(new BsonDocument("_id", doc["_id"]), doc);
            }

            if (executionId is null) return;
            var executions = database.GetCollection<BsonDocument>("WorkflowExecutions");
            var execution = await executions.Find(new BsonDocument("_id", executionId)).FirstAsync();
            WithLegacySettings(execution["WorkflowSnapshot"].AsBsonDocument, shape);
            await executions.ReplaceOneAsync(new BsonDocument("_id", executionId), execution);
        }

        [Theory]
        [MemberData(nameof(LegacySettings))]
        public async Task Old_workflows_versions_and_executions_still_load(string shape)
        {
            await SeedAsync(alertSettings: null);
            var failedId = await RunAAsync();
            await MakeLegacyAsync(shape, failedId);

            var workflow = await _workflows.GetWorkflowAsync(Tenant, "wf-a");
            workflow.Name.Should().Be("Sync orders");
            (workflow.Settings?.Alert).Should().BeNull();
            (await _workflows.GetWorkflowSettingsAsync(Tenant, "wf-a"))?.Alert.Should().BeNull();
            (await _workflows.GetAllWorkflowsAsync(Tenant, 50, 0, null, null, null, null)).Should().HaveCount(2);

            var version = await _versions.GetWorkflowVersionAsync(Tenant, "wf-a-v1");
            version.Snapshot.Nodes.Should().HaveCount(2);
            (version.Snapshot.Settings?.Alert).Should().BeNull();

            var execution = await _executions.GetByIdAsync(failedId, Tenant);
            execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
            (execution.WorkflowSnapshot.Settings?.Alert).Should().BeNull();
        }

        [Theory]
        [MemberData(nameof(LegacySettings))]
        public async Task An_old_workflow_fails_exactly_as_before_and_sends_no_alert(string shape)
        {
            await SeedAsync(alertSettings: null);
            await MakeLegacyAsync(shape);

            var failedId = await RunAAsync();

            var execution = await _executions.GetByIdAsync(failedId, Tenant);
            execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
            execution.ErrorMessage.Should().Be("Order API returned 500");
            execution.NodeExecutions.Single(n => n.NodeId == "fail1").Status.Should().Be(NodeExecutionStatus.Failed);
            execution.ActiveNodeIds.Should().BeEmpty();
            _alerts.Should().BeEmpty();
            (await RunsOfBAsync()).Should().BeEmpty();
        }
    }
}
