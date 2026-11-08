using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using Scheduler.DomainService.Services;
using Workflow.DomainService.Actions;
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
    /// An Action trigger may only be published for an action in the tenant's actions list: otherwise the published
    /// workflow could never run. A listed action whose Workflow switch is off is allowed. A rejected publish changes nothing.
    /// </summary>
    public sealed class ActionTriggerPublishTests : IDisposable
    {
        private const string Tenant = "tenant-wf";

        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IScheduleService> _schedules = new();
        private readonly Mock<IActionsRepository> _actions = new();
        private readonly WorkflowService _service;

        public ActionTriggerPublishTests()
        {
            TestBlocksContext.Set(Tenant, "user-wf");
            _actions.Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new ActionDefinition { Name = "iam.user.created", Workflow = true },
                new ActionDefinition { Name = "iam.user.updated", Workflow = false },
            ]);
            _service = new WorkflowService(_workflows.Object, _versions.Object, _schedules.Object,
                Mock.Of<ILogger<WorkflowService>>(), _actions.Object,
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
                    Name = "Action",
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

        [Theory]
        [InlineData("iam.user.created")]
        [InlineData("iam.user.updated")]
        public async Task A_listed_action_publishes_and_the_node_is_in_the_published_trigger_nodes(string actionName)
        {
            var workflow = Workflow(actionName);
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);

            var result = await _service.PublishNewVersionAsync(Tenant, new WorkflowPublishNewVersionRequestDto { WorkflowId = "wf-1", Name = "v1" });

            result.IsSuccess.Should().BeTrue();
            workflow.IsPublished.Should().BeTrue();
            var trigger = workflow.PublishedMeta!.TriggerNodes.Single();
            trigger.Type.Should().Be("blocksEvent");
            trigger.Parameters["eventName"].AsString.Should().Be(actionName);
        }

        [Theory]
        [InlineData("UserMutationEvent")]
        [InlineData("")]
        public async Task PublishNewVersion_with_an_unknown_or_empty_action_is_rejected_and_changes_nothing(string eventName)
        {
            var workflow = Workflow(eventName, published: true);
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);

            var result = await _service.PublishNewVersionAsync(Tenant, new WorkflowPublishNewVersionRequestDto { WorkflowId = "wf-1", Name = "v2" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain("Message", $"Action '{eventName}' is not in this project's actions");
            workflow.PublishedVersionId.Should().Be("ver-old");
            _schedules.Verify(s => s.DeleteWorkflowSchedulesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
            _versions.Verify(v => v.CreateWorkflowVersionAsync(It.IsAny<WorkflowVersionEntity>()), Times.Never);
            _workflows.Verify(r => r.UpdateWorkflowAsync(It.IsAny<WorkflowEntity>()), Times.Never);
        }

        [Fact]
        public async Task PublishVersion_checks_the_version_snapshot_and_rejects_an_unknown_action()
        {
            // The live graph is fine; the restored version's snapshot listens to an old Blocks Event name.
            var workflow = Workflow("iam.user.created", published: true);
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
            result.Errors.Should().Contain("Message", "Action 'SmsStatusEvent' is not in this project's actions");
            workflow.PublishedVersionId.Should().Be("ver-old");
            _schedules.Verify(s => s.DeleteWorkflowSchedulesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
            _workflows.Verify(r => r.UpdateWorkflowAsync(It.IsAny<WorkflowEntity>()), Times.Never);
        }

        [Fact]
        public async Task A_workflow_without_an_Action_trigger_never_reads_the_actions()
        {
            var workflow = Workflow("iam.user.created");
            workflow.Nodes.RemoveAt(0);
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);

            await _service.PublishNewVersionAsync(Tenant, new WorkflowPublishNewVersionRequestDto { WorkflowId = "wf-1", Name = "v1" });

            _actions.Verify(a => a.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    public class TriggerBlocksEventV2NodeTests
    {
        private static NodeExecutionContext Context(BsonDocument workflowContext) => new()
        {
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            Parameters = new BsonDocument { { "eventName", "iam.user.created" } },
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
        public async Task The_action_item_is_output_on_the_source_branch()
        {
            var item = new BsonDocument { { "id", "evt_1" }, { "action", "iam.user.created" }, { "data", new BsonDocument("userId", "u-1") } };

            var result = await new TriggerBlocksEventV2Node().RunAsync(Context(new BsonDocument("Input", new BsonArray { item })));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().ContainSingle();
            result.OutputItems[0].Branch.Should().Be("source");
            result.OutputItems[0].Data.Output["data"]["userId"].AsString.Should().Be("u-1");
        }

        [Fact]
        public async Task A_missing_input_fails_the_node()
        {
            var result = await new TriggerBlocksEventV2Node().RunAsync(Context(new BsonDocument()));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Theory]
        [InlineData("{\"eventName\":\"iam.user.created\"}", true)]
        [InlineData("{\"EventName\":\"iam.user.created\"}", true)]
        [InlineData("{\"eventName\":\"  \"}", false)]
        [InlineData("{}", false)]
        public async Task ValidateConfiguration_requires_an_action_name(string json, bool expected)
        {
            using var parameters = JsonDocument.Parse(json);

            (await TriggerBlocksEventV2Node.ValidateConfigurationAsync(parameters)).Should().Be(expected);
        }
    }
}
