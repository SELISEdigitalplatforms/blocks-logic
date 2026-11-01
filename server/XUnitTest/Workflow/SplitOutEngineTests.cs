using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes.TransformSplitOutV2;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Split Out through the real engine: a completed upstream node's items are read as input, the new items
    /// are saved with lineage on the "source" branch, and the next node is dispatched.
    /// </summary>
    public class SplitOutEngineTests
    {
        private sealed record Harness(WorkflowEngineService Engine, WorkflowExecutionEntity Execution, List<WorkflowItemExecutionEntity> Saved, List<AddExcuationNodeEvent> Dispatched);

        private static readonly string[] Orders =
        [
            "{ \"orderId\": \"A-100\", \"customer\": \"Ali\", \"lines\": [ { \"sku\": \"PEN\", \"qty\": 2 }, { \"sku\": \"INK\", \"qty\": 1 } ] }",
            "{ \"orderId\": \"A-101\", \"customer\": \"Mina\", \"lines\": [ { \"sku\": \"PAD\", \"qty\": 5 } ] }",
        ];

        private static Harness Build(BsonDocument parameters)
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
                        new NodeEntity { Id = "split", Name = "Split Out", Category = "transform", Type = "splitOut", Version = "v2", Position = new Position(), Parameters = parameters },
                        new NodeEntity { Id = "next", Name = "Code", Category = "transform", Type = "code", Version = "v2", Position = new Position() },
                    ],
                    Edges =
                    [
                        new EdgeEnity { Id = "e1", Source = "src", Target = "split", SourceHandle = "source", TargetHandle = "target" },
                        new EdgeEnity { Id = "e2", Source = "split", Target = "next", SourceHandle = "source", TargetHandle = "target" },
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
                OutputCountsByBranch = new Dictionary<string, int> { ["source"] = Orders.Length },
            });

            var parentItems = Orders.Select((json, i) => new WorkflowItemExecutionEntity
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
                Data = new NodeOutputItemData { Output = BsonDocument.Parse(json) },
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

            var engine = new WorkflowEngineService(repository.Object, [new TransformSplitOutV2Node()], messages.Object,
                NullLogger<WorkflowEngineService>.Instance, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IServiceProvider>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance));
            return new Harness(engine, execution, saved, dispatched);
        }

        private static AddExcuationNodeEvent Event() =>
            new() { TenantId = "t1", WorkflowId = "wf", WorkflowExecutionId = "exec-1", NodeId = "split" };

        private static BsonDocument Params(string include, string outputFieldName = "", params string[] fieldsToInclude) => new()
        {
            { "fields", new BsonArray { new BsonDocument { { "fieldName", "lines" }, { "outputFieldName", outputFieldName } } } },
            { "include", include },
            { "fieldsToInclude", new BsonArray(fieldsToInclude) },
            { "disableDotNotation", false },
        };

        [Fact]
        public async Task TwoOrders_BecomeThreeLineItems_WithLineage_AndTheNextNodeIsDispatched()
        {
            var h = Build(Params("noOtherFields"));

            await h.Engine.RunNodeAsync(Event());

            h.Saved.Select(i => i.Data.Output.ToJson()).Should().Equal(
                "{ \"sku\" : \"PEN\", \"qty\" : 2 }",
                "{ \"sku\" : \"INK\", \"qty\" : 1 }",
                "{ \"sku\" : \"PAD\", \"qty\" : 5 }");
            h.Saved.Should().OnlyContain(i => i.NodeId == "split" && i.Branch == "source");
            h.Saved.Select(i => i.ParentItemIds!.Single()).Should().Equal("src-0", "src-0", "src-1");
            h.Execution.NodeExecutions.Single(ne => ne.NodeId == "split").Status.Should().Be(NodeExecutionStatus.Completed);
            h.Dispatched.Should().ContainSingle().Which.NodeId.Should().Be("next");
        }

        [Fact]
        public async Task AllOtherFields_WithAnOutputFieldName_KeepsTheOrderWithEachLine()
        {
            var h = Build(Params("allOtherFields", "line"));

            await h.Engine.RunNodeAsync(Event());

            h.Saved.Select(i => i.Data.Output.ToJson()).Should().Equal(
                "{ \"orderId\" : \"A-100\", \"customer\" : \"Ali\", \"line\" : { \"sku\" : \"PEN\", \"qty\" : 2 } }",
                "{ \"orderId\" : \"A-100\", \"customer\" : \"Ali\", \"line\" : { \"sku\" : \"INK\", \"qty\" : 1 } }",
                "{ \"orderId\" : \"A-101\", \"customer\" : \"Mina\", \"line\" : { \"sku\" : \"PAD\", \"qty\" : 5 } }");
        }

        [Fact]
        public async Task SelectedOtherFields_CopiesTheListedFields()
        {
            var h = Build(Params("selectedOtherFields", "", "customer"));

            await h.Engine.RunNodeAsync(Event());

            h.Saved.Select(i => i.Data.Output["customer"].AsString).Should().Equal("Ali", "Ali", "Mina");
            h.Saved.Should().OnlyContain(i => !i.Data.Output.AsBsonDocument.Contains("orderId"));
        }

        [Fact]
        public async Task AFieldNoItemHas_CompletesWithNoItems_AndDispatchesNothing()
        {
            var parameters = Params("noOtherFields");
            parameters["fields"][0]["fieldName"] = "tags";
            var h = Build(parameters);

            await h.Engine.RunNodeAsync(Event());

            h.Saved.Should().BeEmpty();
            h.Execution.NodeExecutions.Single(ne => ne.NodeId == "split").Status.Should().Be(NodeExecutionStatus.Completed);
            h.Dispatched.Should().BeEmpty();
        }
    }
}
