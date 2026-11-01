using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Worker.Consumers.Workflow;
using Workflow.DomainService.BlocksEvents;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// The Blocks Event trigger fan-out (blocks-logic#375): every published workflow of the message's tenant that
    /// listens to the event gets one production execution, from its published snapshot, once per EventId.
    /// </summary>
    public class BlocksEventTriggerStartAsyncTests : IDisposable
    {
        private const string Tenant = "tenant-1";
        private const string Event = "UserMutationEvent";
        private static readonly string GrantId = "dg_" + new string('b', 64);

        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IMessageClient> _messages = new();
        private readonly Mock<IBlocksEventCatalogService> _catalog = new();
        private readonly List<WorkflowExecutionEntity> _created = [];
        private readonly List<(ConsumerMessage<AddExcuationNodeEvent> Message, string? Grant)> _sent = [];
        private readonly BlocksEventTriggerService _sut;

        public BlocksEventTriggerStartAsyncTests()
        {
            TestBlocksContext.Set(Tenant);
            DelegatedTokenContext.Clear();
            _executions.Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .ReturnsAsync((WorkflowExecutionEntity e) => { _created.Add(e); return e; });
            _messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()))
                .Callback((ConsumerMessage<AddExcuationNodeEvent> m) => _sent.Add((m, DelegatedTokenContext.Current)))
                .Returns(Task.CompletedTask);
            _catalog.Setup(c => c.IsEnabledForWorkflowAsync(Tenant, Event)).ReturnsAsync(true);

            // The real execution service queues the runs, so the whole path down to the node message is covered.
            var executionService = new WorkflowExecutionService(
                new WorkflowExecutionStores(_workflows.Object, _executions.Object, _versions.Object),
                _messages.Object,
                Mock.Of<ILogger<WorkflowExecutionService>>(),
                Mock.Of<IWorkflowEngineService>(),
                Mock.Of<IWorkflowNotificationService>(),
                new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                new WorkflowTriggerAccess(Mock.Of<IWorkflowAuthService>(), Mock.Of<IDelegationGrantFactory>(), Mock.Of<IInProcessGrantReleaser>(), Mock.Of<IWebhookIntake>()));
            _sut = new BlocksEventTriggerService(
                _workflows.Object,
                _versions.Object,
                _executions.Object,
                executionService,
                _catalog.Object,
                Mock.Of<ILogger<BlocksEventTriggerService>>());
        }

        public void Dispose()
        {
            DelegatedTokenContext.Clear();
            TestBlocksContext.Clear();
        }

        private static NodeEntity BlocksEventNode(string id, string eventName) => new()
        {
            Id = id,
            Name = "Blocks Event",
            Category = "trigger",
            Type = "blocksEvent",
            Version = "v2",
            Position = new Position { X = 0, Y = 0 },
            Parameters = new BsonDocument { { "eventName", eventName } },
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
            _workflows.Setup(r => r.GetPublishWorkflowsByBlocksEventAsync(Tenant, Event))
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
            (Workflow(id, BlocksEventNode("trigger-" + id, Event)), Workflow(id, BlocksEventNode("trigger-" + id, Event)));

        private static BlocksEventTriggerMessage Message(string payloadJson = "{\"UserId\":\"u-9\",\"Action\":1}") => new()
        {
            EventId = "a1b2c3d4e5f60718293a4b5c6d7e8f90",
            EventName = Event,
            SourceService = "blocks-iam",
            ConsumerName = "blocks_iam_listener_user",
            IsMassConsumer = false,
            Timestamp = new DateTime(2026, 10, 31, 9, 30, 0, DateTimeKind.Utc),
            PayloadJson = payloadJson,
        };

        [Fact]
        public async Task H4_every_published_workflow_using_the_event_gets_one_production_execution_and_one_node_message()
        {
            Published(Pair("wf-1"), Pair("wf-2"));

            await _sut.StartAsync(Message());

            _created.Should().HaveCount(2);
            _created.Select(e => e.WorkflowId).Should().BeEquivalentTo(["wf-1", "wf-2"]);
            _created.Should().OnlyContain(e => e.ExecutionMode == WorkflowExecutionMode.Production);
            _created.Should().OnlyContain(e => e.TriggerMetadata.TriggerType == "blocksEvent");
            _sent.Select(s => s.Message.Payload.WorkflowId).Should().BeEquivalentTo(["wf-1", "wf-2"]);
            _sent.Select(s => s.Message.Payload.NodeId).Should().BeEquivalentTo(["trigger-wf-1", "trigger-wf-2"]);
        }

        [Fact]
        public async Task H5_the_input_is_one_item_with_the_event_metadata_and_the_parsed_payload()
        {
            Published(Pair("wf-1"));

            await _sut.StartAsync(Message());

            var execution = _created.Single();
            var input = execution.Context["Input"].AsBsonArray;
            input.Should().HaveCount(1);
            var item = input[0].AsBsonDocument;
            item["EventId"].AsString.Should().Be("a1b2c3d4e5f60718293a4b5c6d7e8f90");
            item["EventName"].AsString.Should().Be(Event);
            item["SourceService"].AsString.Should().Be("blocks-iam");
            item["ConsumerName"].AsString.Should().Be("blocks_iam_listener_user");
            item["IsMassConsumer"].AsBoolean.Should().BeFalse();
            item["Timestamp"].AsString.Should().Be("2026-10-31T09:30:00.0000000Z");
            item["Payload"].AsBsonDocument["UserId"].AsString.Should().Be("u-9");
            execution.TriggerMetadata.TriggerData.Should().BeEquivalentTo(input);
            execution.Status.Should().Be(WorkflowExecutionStatus.Queued);
            execution.ActiveNodeIds.Should().Equal("trigger-wf-1");
        }

        [Fact]
        public async Task H17_the_node_message_is_sent_while_the_inbound_delegation_grant_is_current()
        {
            Published(Pair("wf-1"));
            DelegatedTokenContext.Set(GrantId);

            await _sut.StartAsync(Message());

            // The Genesis message client stamps the DelegationGrant header from this context at send time.
            _sent.Single().Grant.Should().Be(GrantId);
        }

        [Fact]
        public async Task H17_without_an_inbound_grant_the_node_message_carries_none()
        {
            Published(Pair("wf-1"));

            await _sut.StartAsync(Message());

            _sent.Single().Grant.Should().BeNull();
        }

        [Fact]
        public async Task C1_a_redelivered_EventId_starts_no_second_execution_for_that_workflow()
        {
            Published(Pair("wf-1"), Pair("wf-2"));
            _executions.Setup(r => r.ExistsForBlocksEventAsync(Tenant, "wf-1", "a1b2c3d4e5f60718293a4b5c6d7e8f90")).ReturnsAsync(true);

            await _sut.StartAsync(Message());

            _created.Select(e => e.WorkflowId).Should().Equal("wf-2");
            _sent.Should().ContainSingle();
        }

        [Fact]
        public async Task C2_an_event_disabled_since_it_was_forwarded_starts_nothing()
        {
            Published(Pair("wf-1"));
            _catalog.Setup(c => c.IsEnabledForWorkflowAsync(Tenant, Event)).ReturnsAsync(false);

            await _sut.StartAsync(Message());

            _created.Should().BeEmpty();
            _workflows.Verify(r => r.GetPublishWorkflowsByBlocksEventAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task C3_a_message_without_a_tenant_starts_nothing_and_reads_nothing()
        {
            TestBlocksContext.Clear();

            await _sut.StartAsync(Message());

            _created.Should().BeEmpty();
            _catalog.Verify(c => c.IsEnabledForWorkflowAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _workflows.Verify(r => r.GetPublishWorkflowsByBlocksEventAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task C4_one_failing_workflow_does_not_stop_the_others()
        {
            Published(Pair("wf-1"), Pair("wf-2"), Pair("wf-3"));
            _executions.Setup(r => r.UpdateAsync(It.Is<WorkflowExecutionEntity>(e => e.WorkflowId == "wf-2")))
                .ThrowsAsync(new InvalidOperationException("boom"));

            await _sut.StartAsync(Message());

            _created.Should().HaveCount(3);
            _sent.Select(s => s.Message.Payload.WorkflowId).Should().BeEquivalentTo(["wf-1", "wf-3"]);
        }

        [Fact]
        public async Task C5_no_published_workflow_using_the_event_finishes_quietly()
        {
            Published();

            var act = () => _sut.StartAsync(Message());

            await act.Should().NotThrowAsync();
            _created.Should().BeEmpty();
            _versions.Verify(r => r.GetWorkflowVersionsAsync(It.IsAny<string>(), It.IsAny<string[]>()), Times.Never);
        }

        [Fact]
        public async Task C7_the_published_snapshot_runs_not_the_draft()
        {
            // The draft switched to another event after publishing; the published snapshot still listens to this one.
            var draft = Workflow("wf-1", BlocksEventNode("draft-node", "PaymentLifecycleEvent"));
            var snapshot = Workflow("wf-1", BlocksEventNode("published-node", Event));
            Published((draft, snapshot));

            await _sut.StartAsync(Message());

            _created.Single().WorkflowSnapshot.Should().BeSameAs(snapshot);
            _sent.Single().Message.Payload.NodeId.Should().Be("published-node");
        }

        [Fact]
        public async Task C7_a_snapshot_whose_node_listens_to_another_event_is_skipped()
        {
            var snapshot = Workflow("wf-1", BlocksEventNode("n", "PaymentLifecycleEvent"));
            Published((Workflow("wf-1", BlocksEventNode("n", Event)), snapshot));

            await _sut.StartAsync(Message());

            _created.Should().BeEmpty();
        }

        [Fact]
        public async Task A7_two_nodes_for_the_same_event_start_the_workflow_once_from_the_first()
        {
            var both = Workflow("wf-1", BlocksEventNode("first", Event), BlocksEventNode("second", Event));
            Published((both, both));

            await _sut.StartAsync(Message());

            _created.Should().ContainSingle();
            _sent.Single().Message.Payload.NodeId.Should().Be("first");
        }

        [Fact]
        public async Task A_failing_dedupe_lookup_skips_only_that_workflow()
        {
            Published(Pair("wf-1"), Pair("wf-2"));
            _executions.Setup(r => r.ExistsForBlocksEventAsync(Tenant, "wf-1", It.IsAny<string>())).ThrowsAsync(new TimeoutException("db"));

            await _sut.StartAsync(Message());

            _created.Select(e => e.WorkflowId).Should().Equal("wf-2");
        }

        [Fact]
        public async Task C15_only_the_message_tenant_is_read()
        {
            Published(Pair("wf-1"));

            await _sut.StartAsync(Message());

            _catalog.Verify(c => c.IsEnabledForWorkflowAsync(It.Is<string>(t => t != Tenant), It.IsAny<string>()), Times.Never);
            _workflows.Verify(r => r.GetPublishWorkflowsByBlocksEventAsync(It.Is<string>(t => t != Tenant), It.IsAny<string>()), Times.Never);
            _versions.Verify(r => r.GetWorkflowVersionsAsync(It.Is<string>(t => t != Tenant), It.IsAny<string[]>()), Times.Never);
            _executions.Verify(r => r.ExistsForBlocksEventAsync(It.Is<string>(t => t != Tenant), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("[1,2,3]")]
        [InlineData("not json")]
        [InlineData("42")]
        [InlineData("")]
        public async Task C16_a_payload_that_is_not_a_JSON_object_is_passed_on_as_the_raw_string(string payloadJson)
        {
            Published(Pair("wf-1"));

            await _sut.StartAsync(Message(payloadJson));

            var payload = _created.Single().Context["Input"].AsBsonArray[0]["Payload"];
            payload.IsString.Should().BeTrue();
            payload.AsString.Should().Be(payloadJson);
            _sent.Should().ContainSingle();
        }

        [Fact]
        public async Task The_consumer_hands_the_message_to_the_fan_out()
        {
            var service = new Mock<IBlocksEventTriggerService>();
            var consumer = new BlocksEventTriggerConsumer(NullLogger<BlocksEventTriggerConsumer>.Instance, service.Object);
            var message = Message();

            await consumer.Consume(message);

            service.Verify(s => s.StartAsync(message), Times.Once);
        }
    }
}
