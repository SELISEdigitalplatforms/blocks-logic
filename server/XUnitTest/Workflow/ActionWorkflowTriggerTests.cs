using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Functions.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using StackExchange.Redis;
using Worker.Consumers.Actions;
using Workflow.DomainService.Actions;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// The Action trigger for workflows: every published workflow of the message's tenant whose Action trigger names the
    /// action gets one production execution, from its published snapshot, once per event id, while the tenant's
    /// Workflow switch for the action is on.
    /// </summary>
    public sealed class ActionWorkflowTriggerTests : IDisposable
    {
        private const string Tenant = "tenant-1";
        private const string ActionName = "iam.user.created";
        private const string EventId = "evt_a1b2c3d4e5f60718293a4b5c6d7e8f90";
        private static readonly string GrantId = "dg_" + new string('b', 64);

        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IMessageClient> _messages = new();
        private readonly Mock<IActionsRepository> _actions = new();
        private readonly Mock<IActionStartGuard> _guard = new();
        private readonly List<WorkflowExecutionEntity> _created = [];
        private readonly List<(ConsumerMessage<AddExcuationNodeEvent> Message, string? Grant)> _sent = [];
        private readonly ActionWorkflowTriggerService _sut;

        public ActionWorkflowTriggerTests()
        {
            TestBlocksContext.Set(Tenant);
            DelegatedTokenContext.Clear();
            _executions.Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .ReturnsAsync((WorkflowExecutionEntity e) => { _created.Add(e); return e; });
            _messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()))
                .Callback((ConsumerMessage<AddExcuationNodeEvent> m) => _sent.Add((m, DelegatedTokenContext.Current)))
                .Returns(Task.CompletedTask);
            Switch(workflow: true);
            _guard.Setup(g => g.TryClaimAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(Mock.Of<IAsyncDisposable>());

            // The real execution service queues the runs, so the whole path down to the node message is covered.
            var executionService = new WorkflowExecutionService(
                new WorkflowExecutionStores(_workflows.Object, _executions.Object, _versions.Object),
                _messages.Object,
                Mock.Of<ILogger<WorkflowExecutionService>>(),
                Mock.Of<IWorkflowEngineService>(),
                Mock.Of<IWorkflowNotificationService>(),
                new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                new WorkflowTriggerAccess(Mock.Of<IWorkflowAuthService>(), Mock.Of<IDelegationGrantFactory>(), Mock.Of<IInProcessGrantReleaser>(), Mock.Of<IWebhookIntake>()));
            _sut = new ActionWorkflowTriggerService(
                _workflows.Object,
                _versions.Object,
                executionService,
                _actions.Object,
                _guard.Object,
                Mock.Of<ILogger<ActionWorkflowTriggerService>>());
        }

        public void Dispose()
        {
            DelegatedTokenContext.Clear();
            TestBlocksContext.Clear();
        }

        private void Switch(bool workflow, bool function = false) =>
            _actions.Setup(a => a.FindAsync(ActionName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActionDefinition { Name = ActionName, Label = "User created", Service = "iam", Workflow = workflow, Function = function });

        private static NodeEntity ActionNode(string id, string actionName) => new()
        {
            Id = id,
            Name = "Action",
            Category = "trigger",
            Type = "blocksEvent",
            Version = "v2",
            Position = new Position { X = 0, Y = 0 },
            Parameters = new BsonDocument { { "eventName", actionName } },
        };

        private static WorkflowEntity Workflow(string id, params NodeEntity[] nodes) => new()
        {
            ItemId = id,
            Name = id,
            TenantId = Tenant,
            IsPublished = true,
            PublishedVersionId = "ver-" + id,
            Nodes = nodes.ToList(),
            Edges = new(),
            Settings = new(),
        };

        /// <summary>The draft (returned by the query) and its published snapshot (returned by the version lookup).</summary>
        private void Published(params (WorkflowEntity Draft, WorkflowEntity Snapshot)[] workflows)
        {
            _workflows.Setup(r => r.GetPublishedWorkflowsByActionAsync(Tenant, ActionName))
                .ReturnsAsync(workflows.Select(w => w.Draft).ToList());
            _versions.Setup(r => r.GetWorkflowVersionsAsync(Tenant, It.IsAny<string[]>()))
                .ReturnsAsync(workflows.Select(w => new WorkflowVersionEntity
                {
                    TenantId = Tenant,
                    ItemId = w.Draft.PublishedVersionId!,
                    WorkflowId = w.Draft.ItemId,
                    Name = "v1",
                    Snapshot = w.Snapshot,
                }).ToList());
        }

        private static (WorkflowEntity, WorkflowEntity) Pair(string id) =>
            (Workflow(id, ActionNode("trigger-" + id, ActionName)), Workflow(id, ActionNode("trigger-" + id, ActionName)));

        private static BlocksAction Action(string dataJson = """{"userId":"u-9","mfaEnabled":true}""", string? organizationId = "org-7") => new()
        {
            Id = EventId,
            Action = ActionName,
            Time = new DateTime(2026, 11, 4, 9, 20, 0, DateTimeKind.Utc),
            OrganizationId = organizationId,
            DataJson = dataJson,
        };

        [Fact]
        public async Task Every_published_workflow_using_the_action_gets_one_production_execution()
        {
            Published(Pair("wf-1"), Pair("wf-2"));

            await _sut.StartAsync(Action());

            _created.Select(e => e.WorkflowId).Should().BeEquivalentTo("wf-1", "wf-2");
            _created.Should().OnlyContain(e => e.ExecutionMode == WorkflowExecutionMode.Production);
            _created.Should().OnlyContain(e => e.TriggerMetadata.TriggerType == "blocksEvent");
            _sent.Select(s => s.Message.Payload.NodeId).Should().BeEquivalentTo("trigger-wf-1", "trigger-wf-2");
        }

        [Fact]
        public async Task The_input_is_the_simple_action_json_so_expressions_read_trigger_data_fields()
        {
            Published(Pair("wf-1"));

            await _sut.StartAsync(Action());

            var execution = _created.Single();
            var item = execution.Context["Input"].AsBsonArray.Single().AsBsonDocument;
            item.Names.Should().Equal("id", "action", "time", "organizationId", "data");
            item["id"].AsString.Should().Be(EventId);
            item["action"].AsString.Should().Be(ActionName);
            item["time"].AsString.Should().Be("2026-11-04T09:20:00.000Z");
            item["organizationId"].AsString.Should().Be("org-7");
            item["data"]["userId"].AsString.Should().Be("u-9");
            item["data"]["mfaEnabled"].AsBoolean.Should().BeTrue();
            execution.ActiveNodeIds.Should().Equal("trigger-wf-1");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task No_organization_is_null_in_the_input(string? organizationId)
        {
            Published(Pair("wf-1"));

            await _sut.StartAsync(Action(organizationId: organizationId));

            _created.Single().Context["Input"][0]["organizationId"].IsBsonNull.Should().BeTrue();
        }

        [Theory]
        [InlineData("[1,2]")]
        [InlineData("not json")]
        [InlineData("")]
        public async Task Data_that_is_not_a_json_object_becomes_an_empty_object(string dataJson)
        {
            Published(Pair("wf-1"));

            await _sut.StartAsync(Action(dataJson));

            _created.Single().Context["Input"][0]["data"].AsBsonDocument.ElementCount.Should().Be(0);
        }

        [Fact]
        public async Task The_node_message_is_sent_while_the_inbound_delegation_grant_is_current()
        {
            Published(Pair("wf-1"));
            DelegatedTokenContext.Set(GrantId);

            await _sut.StartAsync(Action());

            _sent.Single().Grant.Should().Be(GrantId);
        }

        [Fact]
        public async Task An_event_id_that_already_started_a_workflow_starts_no_second_execution_for_it()
        {
            Published(Pair("wf-1"), Pair("wf-2"));
            _guard.Setup(g => g.TryClaimAsync(Tenant, "wf-1", EventId)).ReturnsAsync((IAsyncDisposable?)null);

            await _sut.StartAsync(Action());

            _created.Select(e => e.WorkflowId).Should().Equal("wf-2");
        }

        [Fact]
        public async Task The_claim_is_released_after_the_start()
        {
            Published(Pair("wf-1"));
            var claim = new Mock<IAsyncDisposable>();
            _guard.Setup(g => g.TryClaimAsync(Tenant, "wf-1", EventId)).ReturnsAsync(claim.Object);

            await _sut.StartAsync(Action());

            claim.Verify(c => c.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task A_workflow_switch_turned_off_after_sending_starts_nothing()
        {
            Published(Pair("wf-1"));
            Switch(workflow: false, function: true);

            await _sut.StartAsync(Action());

            _created.Should().BeEmpty();
            _workflows.Verify(r => r.GetPublishedWorkflowsByActionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task An_action_missing_from_the_tenant_list_starts_nothing()
        {
            Published(Pair("wf-1"));
            _actions.Setup(a => a.FindAsync(ActionName, It.IsAny<CancellationToken>())).ReturnsAsync((ActionDefinition?)null);

            await _sut.StartAsync(Action());

            _created.Should().BeEmpty();
        }

        [Fact]
        public async Task A_message_without_a_tenant_starts_nothing_and_reads_nothing()
        {
            TestBlocksContext.Clear();

            await _sut.StartAsync(Action());

            _created.Should().BeEmpty();
            _actions.Verify(a => a.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task One_failing_workflow_does_not_stop_the_others()
        {
            Published(Pair("wf-1"), Pair("wf-2"), Pair("wf-3"));
            _executions.Setup(r => r.UpdateAsync(It.Is<WorkflowExecutionEntity>(e => e.WorkflowId == "wf-2")))
                .ThrowsAsync(new InvalidOperationException("boom"));

            await _sut.StartAsync(Action());

            _sent.Select(s => s.Message.Payload.WorkflowId).Should().BeEquivalentTo("wf-1", "wf-3");
        }

        [Fact]
        public async Task A_failing_dedupe_check_skips_only_that_workflow()
        {
            Published(Pair("wf-1"), Pair("wf-2"));
            _guard.Setup(g => g.TryClaimAsync(Tenant, "wf-1", EventId)).ThrowsAsync(new TimeoutException("redis"));

            await _sut.StartAsync(Action());

            _created.Select(e => e.WorkflowId).Should().Equal("wf-2");
        }

        [Fact]
        public async Task The_published_snapshot_runs_not_the_draft()
        {
            // The draft switched to another action after publishing; the published snapshot still listens to this one.
            var draft = Workflow("wf-1", ActionNode("draft-node", "iam.user.updated"));
            var snapshot = Workflow("wf-1", ActionNode("published-node", ActionName));
            Published((draft, snapshot));

            await _sut.StartAsync(Action());

            _created.Single().WorkflowSnapshot.Should().BeSameAs(snapshot);
            _sent.Single().Message.Payload.NodeId.Should().Be("published-node");
        }

        [Fact]
        public async Task Two_nodes_for_the_same_action_start_the_workflow_once_from_the_first()
        {
            var both = Workflow("wf-1", ActionNode("first", ActionName), ActionNode("second", ActionName));
            Published((both, both));

            await _sut.StartAsync(Action());

            _sent.Single().Message.Payload.NodeId.Should().Be("first");
        }

        [Fact]
        public async Task The_consumer_runs_the_workflow_side_and_the_function_side()
        {
            var workflows = new Mock<IActionWorkflowTriggerService>();
            var functions = new Mock<IFunctionActionTriggerService>();
            var consumer = new BlocksActionConsumer(workflows.Object, functions.Object, NullLogger<BlocksActionConsumer>.Instance);
            var action = Action();

            await consumer.Consume(action);

            workflows.Verify(w => w.StartAsync(action), Times.Once);
            functions.Verify(f => f.StartAsync(action), Times.Once);
        }

        [Fact]
        public async Task The_consumer_still_runs_functions_when_the_workflow_side_throws()
        {
            var workflows = new Mock<IActionWorkflowTriggerService>();
            workflows.Setup(w => w.StartAsync(It.IsAny<BlocksAction>())).ThrowsAsync(new TimeoutException("mongo"));
            var functions = new Mock<IFunctionActionTriggerService>();
            var consumer = new BlocksActionConsumer(workflows.Object, functions.Object, NullLogger<BlocksActionConsumer>.Instance);

            await consumer.Consume(Action());

            functions.Verify(f => f.StartAsync(It.IsAny<BlocksAction>()), Times.Once);
        }
    }

    /// <summary>
    /// One execution per action event and workflow, also when Genesis delivers the same event twice at once (both logic
    /// queues on, or a redelivery).
    /// </summary>
    public class ActionStartGuardTests
    {
        private const string Tenant = "tenant-1";

        private readonly (IDatabase Database, global::XUnitTest.Functions.FakeRedisDatabase Fake) _redis = global::XUnitTest.Functions.FakeRedisDatabase.Create();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly List<string> _deleted = [];
        private bool _lockFree = true;

        private ActionStartGuard Guard()
        {
            _redis.Fake.On("StringSetAsync", _ => _lockFree);
            _redis.Fake.On("KeyDeleteAsync", a => { _deleted.Add(a[0]!.ToString()); return true; });
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            return new ActionStartGuard(cache.Object, _executions.Object, NullLogger<ActionStartGuard>.Instance);
        }

        [Fact]
        public async Task A_new_event_is_claimed_and_released_on_dispose()
        {
            var guard = Guard();

            var claim = await guard.TryClaimAsync(Tenant, "wf-1", "evt_1");

            claim.Should().NotBeNull();
            await claim.DisposeAsync();
            _deleted.Should().Equal(ActionStartGuard.LockKey(Tenant, "wf-1", "evt_1"));
        }

        [Fact]
        public async Task A_delivery_arriving_while_another_starts_the_same_workflow_is_refused()
        {
            var guard = Guard();
            _lockFree = false;

            (await guard.TryClaimAsync(Tenant, "wf-1", "evt_1")).Should().BeNull();
            _executions.Verify(e => e.ExistsForActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task An_event_that_already_has_an_execution_is_refused_and_the_lock_released()
        {
            var guard = Guard();
            _executions.Setup(e => e.ExistsForActionAsync(Tenant, "wf-1", "evt_1")).ReturnsAsync(true);

            (await guard.TryClaimAsync(Tenant, "wf-1", "evt_1")).Should().BeNull();
            _deleted.Should().ContainSingle();
        }
    }
}
