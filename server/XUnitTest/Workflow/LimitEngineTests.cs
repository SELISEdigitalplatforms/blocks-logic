using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes.TransformLimitV2;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Limit through the real engine: the upstream node's items are read as input, the kept items are saved
    /// with lineage on the "source" branch, and the next node is dispatched only when something was kept.
    /// </summary>
    public class LimitEngineTests
    {
        private sealed record Harness(WorkflowEngineService Engine, WorkflowExecutionEntity Execution, List<WorkflowItemExecutionEntity> Saved, List<AddExcuationNodeEvent> Dispatched);

        private static Harness Build(BsonDocument parameters, int inputCount = 5)
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
                    Nodes =
                    [
                        new NodeEntity { Id = "src", Name = "Webhook", Category = "trigger", Type = "webhook", Version = "v2", Position = new Position() },
                        new NodeEntity { Id = "limit", Name = "Limit", Category = "transform", Type = "limit", Version = "v2", Position = new Position(), Parameters = parameters },
                        new NodeEntity { Id = "next", Name = "Code", Category = "transform", Type = "code", Version = "v2", Position = new Position() },
                    ],
                    Edges =
                    [
                        new EdgeEnity { Id = "e1", Source = "src", Target = "limit", SourceHandle = "source", TargetHandle = "target" },
                        new EdgeEnity { Id = "e2", Source = "limit", Target = "next", SourceHandle = "source", TargetHandle = "target" },
                    ],
                    TestMeta = new TestWorkflowMeta(),
                },
            };
            execution.NodeExecutions.Add(new NodeExecutionEntity
            {
                Id = "ne-src",
                NodeId = "src",
                NodeName = "Webhook",
                NodeType = "webhook",
                NodeVersion = "v2",
                Status = NodeExecutionStatus.Completed,
                OutputCountsByBranch = new Dictionary<string, int> { ["source"] = inputCount },
            });

            var parentItems = Enumerable.Range(0, inputCount).Select(i => new WorkflowItemExecutionEntity
            {
                Id = $"src-{i}",
                WorkflowExecutionId = "exec-1",
                TenantId = "t1",
                NodeId = "src",
                NodeExecutionId = "ne-src",
                NodeName = "Webhook",
                Branch = "source",
                ItemIndex = i,
                ParentItemIds = new List<string>(),
                AncestorMap = new Dictionary<string, string>(),
                Data = new NodeOutputItemData { Output = new BsonDocument("id", i + 1) },
            }).ToList();

            var saved = new List<WorkflowItemExecutionEntity>();
            var dispatched = new List<AddExcuationNodeEvent>();
            var repository = new Mock<IWorkflowExecutionRepository> { DefaultValue = DefaultValue.Empty };
            repository.Setup(r => r.GetByIdAsync("exec-1", "t1")).ReturnsAsync(execution);
            repository.Setup(r => r.GetItemsByNodeIdsAsync(It.IsAny<string>(), It.IsAny<List<Dictionary<string, string>>>(), It.IsAny<string>()))
                .ReturnsAsync((string _, List<Dictionary<string, string>> pairs, string _) =>
                    parentItems.Where(item => pairs.Any(p => p["NodeId"] == item.NodeId && p["Branch"] == item.Branch)).ToList());
            repository.Setup(r => r.TryAddFirstNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            repository.Setup(r => r.TryAddNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            repository.Setup(r => r.AddItemsAsync(It.IsAny<string>(), It.IsAny<List<WorkflowItemExecutionEntity>>()))
                .Callback((string _, List<WorkflowItemExecutionEntity> added) => saved.AddRange(added))
                .Returns(Task.CompletedTask);

            var messages = new Mock<IMessageClient>();
            messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()))
                .Callback((ConsumerMessage<AddExcuationNodeEvent> message) => dispatched.Add(message.Payload))
                .Returns(Task.CompletedTask);

            var engine = new WorkflowEngineService(repository.Object, [new TransformLimitV2Node()], messages.Object,
                NullLogger<WorkflowEngineService>.Instance, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IServiceProvider>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance));
            return new Harness(engine, execution, saved, dispatched);
        }

        private static AddExcuationNodeEvent Event() =>
            new() { TenantId = "t1", WorkflowId = "wf", WorkflowExecutionId = "exec-1", NodeId = "limit" };

        private static BsonDocument Params(int maxItems, string keep) => new() { { "maxItems", maxItems }, { "keep", keep } };

        [Fact]
        public async Task LastTwo_AreSavedWithLineage_AndTheNextNodeIsDispatched()
        {
            var h = Build(Params(2, "last"));

            await h.Engine.RunNodeAsync(Event());

            h.Saved.Select(i => i.Data.Output["id"].AsInt32).Should().Equal(4, 5);
            h.Saved.Should().OnlyContain(i => i.NodeId == "limit" && i.Branch == "source");
            h.Saved.Select(i => i.ParentItemIds!.Single()).Should().Equal("src-3", "src-4");
            h.Execution.NodeExecutions.Single(ne => ne.NodeId == "limit").Status.Should().Be(NodeExecutionStatus.Completed);
            h.Dispatched.Should().ContainSingle().Which.NodeId.Should().Be("next");
        }

        [Fact]
        public async Task FirstThree_AreSavedInInputOrder()
        {
            var h = Build(Params(3, "first"));

            await h.Engine.RunNodeAsync(Event());

            h.Saved.Select(i => i.Data.Output["id"].AsInt32).Should().Equal(1, 2, 3);
        }

        [Fact]
        public async Task NoInputItems_CompletesWithNoItems_AndDispatchesNothing()
        {
            var h = Build(Params(3, "first"), inputCount: 0);

            await h.Engine.RunNodeAsync(Event());

            h.Saved.Should().BeEmpty();
            h.Execution.NodeExecutions.Single(ne => ne.NodeId == "limit").Status.Should().Be(NodeExecutionStatus.Completed);
            h.Dispatched.Should().BeEmpty();
        }

        [Fact]
        public async Task MaxItemsZero_FailsTheStep_AndDispatchesNothing()
        {
            var h = Build(Params(0, "first"));

            await h.Engine.RunNodeAsync(Event());

            h.Saved.Should().BeEmpty();
            h.Dispatched.Should().BeEmpty();
        }
    }
}
