using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using Scheduler.DomainService.Services;
using Workflow.DomainService.BlocksEvents;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.TriggerBlocksEventV2;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// A Blocks Event trigger may only be published for an event enabled for workflows (blocks-logic#375 A10):
    /// otherwise the published workflow would silently never run. A rejected publish changes nothing.
    /// </summary>
    public class BlocksEventTriggerPublishTests : IDisposable
    {
        private const string Tenant = "tenant-wf";

        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IScheduleService> _schedules = new();
        private readonly Mock<IBlocksEventCatalogService> _catalog = new();
        private readonly WorkflowService _service;

        public BlocksEventTriggerPublishTests()
        {
            TestBlocksContext.Set(Tenant, "user-wf");
            _catalog.Setup(c => c.IsEnabledForWorkflowAsync(Tenant, "UserMutationEvent")).ReturnsAsync(true);
            _service = new WorkflowService(_workflows.Object, _versions.Object, _schedules.Object,
                Mock.Of<ILogger<WorkflowService>>(), _catalog.Object,
                Mock.Of<FluentValidation.IValidator<global::Workflow.DomainService.Dtos.WorkflowUpdateSettingsRequestDto>>());
        }

        public void Dispose() => TestBlocksContext.Clear();

        private static WorkflowEntity Workflow(string eventName, bool published = false) => new()
        {
            ItemId = "wf-1",
            Name = "Workflow",
            TenantId = Tenant,
            IsPublished = published,
            PublishedVersionId = published ? "ver-old" : null,
            LastPublishedVersionId = published ? "ver-old" : null,
            Nodes =
            [
                new()
                {
                    Id = "node-1",
                    Name = "Blocks Event",
                    Category = "trigger",
                    Type = "blocksEvent",
                    Version = "v2",
                    Position = new Position { X = 0, Y = 0 },
                    Parameters = new BsonDocument { { "eventName", eventName } },
                },
                new()
                {
                    Id = "node-2",
                    Name = "Set",
                    Category = "transform",
                    Type = "setField",
                    Version = "v2",
                    Position = new Position { X = 100, Y = 0 },
                    Parameters = new BsonDocument(),
                },
            ],
            Edges = new(),
            Settings = new(),
        };

        [Fact]
        public async Task H3_an_enabled_event_publishes_and_the_node_is_in_the_published_trigger_nodes()
        {
            var workflow = Workflow("UserMutationEvent");
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);

            var result = await _service.PublishNewVersionAsync(Tenant, new WorkflowPublishNewVersionRequestDto { WorkflowId = "wf-1", Name = "v1" });

            result.IsSuccess.Should().BeTrue();
            workflow.IsPublished.Should().BeTrue();
            var trigger = workflow.PublishedMeta!.TriggerNodes.Single();
            trigger.Type.Should().Be("blocksEvent");
            trigger.Parameters["eventName"].AsString.Should().Be("UserMutationEvent");
        }

        [Theory]
        [InlineData("SmsStatusEvent")]
        [InlineData("")]
        public async Task C6_PublishNewVersion_with_a_disabled_or_empty_event_is_rejected_and_changes_nothing(string eventName)
        {
            var workflow = Workflow(eventName, published: true);
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);

            var result = await _service.PublishNewVersionAsync(Tenant, new WorkflowPublishNewVersionRequestDto { WorkflowId = "wf-1", Name = "v2" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain("Message", $"Event '{eventName}' is not enabled for workflows");
            workflow.PublishedVersionId.Should().Be("ver-old");
            _schedules.Verify(s => s.DeleteWorkflowSchedulesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
            _versions.Verify(v => v.CreateWorkflowVersionAsync(It.IsAny<WorkflowVersionEntity>()), Times.Never);
            _workflows.Verify(r => r.UpdateWorkflowAsync(It.IsAny<WorkflowEntity>()), Times.Never);
        }

        [Fact]
        public async Task C6_PublishVersion_checks_the_version_snapshot_and_rejects_a_disabled_event()
        {
            // The live graph is fine; the restored version's snapshot listens to a disabled event.
            var workflow = Workflow("UserMutationEvent", published: true);
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);
            _versions.Setup(v => v.GetWorkflowVersionAsync(Tenant, "ver-2")).ReturnsAsync(new WorkflowVersionEntity
            {
                TenantId = Tenant,
                ItemId = "ver-2",
                WorkflowId = "wf-1",
                Name = "v2",
                Snapshot = Workflow("SmsStatusEvent"),
            });

            var result = await _service.PublishVersionAsync(Tenant, new WorkflowPublishVersionRequestDto { WorkflowId = "wf-1", VersionId = "ver-2" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain("Message", "Event 'SmsStatusEvent' is not enabled for workflows");
            workflow.PublishedVersionId.Should().Be("ver-old");
            _schedules.Verify(s => s.DeleteWorkflowSchedulesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
            _workflows.Verify(r => r.UpdateWorkflowAsync(It.IsAny<WorkflowEntity>()), Times.Never);
        }

        [Fact]
        public async Task A_workflow_without_a_Blocks_Event_trigger_never_reads_the_catalog()
        {
            var workflow = Workflow("UserMutationEvent");
            workflow.Nodes.RemoveAt(0);
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);

            await _service.PublishNewVersionAsync(Tenant, new WorkflowPublishNewVersionRequestDto { WorkflowId = "wf-1", Name = "v1" });

            _catalog.Verify(c => c.IsEnabledForWorkflowAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }

    public class TriggerBlocksEventV2NodeTests
    {
        private static NodeExecutionContext Context(BsonDocument workflowContext) => new()
        {
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            Parameters = new BsonDocument { { "eventName", "UserMutationEvent" } },
            InputItems = new List<WorkflowItemExecutionEntity>(),
            IterationCount = 0,
            WorkflowContext = workflowContext,
        };

        [Fact]
        public void NodeMetadata_IsExpected()
        {
            var node = new TriggerBlocksEventV2Node();
            node.NodeType.Should().Be("blocksEvent");
            node.Version.Should().Be("v2");
            // A new type: v2 only, like Switch, Split Out and Merge.
            ((INodeExecutor)node).Versions.Should().Equal("v2");
        }

        [Fact]
        public async Task H5_the_event_item_is_output_on_the_source_branch()
        {
            var item = new BsonDocument { { "EventId", "e1" }, { "EventName", "UserMutationEvent" }, { "Payload", new BsonDocument("UserId", "u-1") } };

            var result = await new TriggerBlocksEventV2Node().RunAsync(Context(new BsonDocument("Input", new BsonArray { item })));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().ContainSingle();
            result.OutputItems[0].Branch.Should().Be("source");
            result.OutputItems[0].Data.Output["Payload"]["UserId"].AsString.Should().Be("u-1");
        }

        [Fact]
        public async Task A_missing_input_fails_the_node()
        {
            var result = await new TriggerBlocksEventV2Node().RunAsync(Context(new BsonDocument()));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Theory]
        [InlineData("{\"eventName\":\"UserMutationEvent\"}", true)]
        [InlineData("{\"EventName\":\"UserMutationEvent\"}", true)]
        [InlineData("{\"eventName\":\"  \"}", false)]
        [InlineData("{}", false)]
        public async Task ValidateConfiguration_requires_an_event_name(string json, bool expected)
        {
            using var parameters = JsonDocument.Parse(json);

            (await TriggerBlocksEventV2Node.ValidateConfigurationAsync(parameters)).Should().Be(expected);
        }
    }
}
