using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Step test with pinned data: pinned means "use this data, don't run". An action node (the ones
    /// that act on the outside world) is not called; its output is built from the pin data alone.
    /// </summary>
    public class PinnedStepTests
    {
        private static NodeEntity Node(string category, BsonArray? pin) => new()
        {
            Id = "n1",
            Name = "Charge",
            Category = category,
            Type = "function",
            Version = "v1",
            Position = new Position(),
            Parameters = new BsonDocument("FunctionId", "fn_1"),
            PinData = pin,
        };

        private static WorkflowItemExecutionEntity Input(string id, BsonDocument output) => new()
        {
            Id = id,
            WorkflowExecutionId = "exec-1",
            TenantId = "t1",
            NodeId = "up",
            NodeExecutionId = "ne-up",
            NodeName = "Up",
            Branch = "source",
            Data = new NodeOutputItemData { Output = output },
        };

        private static NodeExecutionContext Context(params WorkflowItemExecutionEntity[] inputs) => new()
        {
            WorkflowExecutionId = "exec-1",
            TenantId = "t1",
            Parameters = new BsonDocument(),
            InputItems = inputs.ToList(),
            IterationCount = inputs.Length,
            WorkflowContext = new BsonDocument(),
            AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
        };

        [Theory]
        [InlineData("action", true)]
        [InlineData("Action", true)]
        [InlineData("logic", false)]      // decides branches, no side effects: still runs
        [InlineData("transform", false)]
        [InlineData("trigger", false)]
        public void Only_action_nodes_are_skipped_when_pinned(string category, bool skipped)
        {
            WorkflowEngineService.SkipsRunWhenPinned(Node(category, new BsonArray { new BsonDocument() })).Should().Be(skipped);
        }

        [Fact]
        public void A_pinned_action_node_outputs_one_item_per_pin_entry_tied_to_its_input()
        {
            var pin = new BsonArray { new BsonDocument("charged", 1), new BsonDocument("charged", 2) };
            var context = Context(Input("i1", new BsonDocument("order", 1)), Input("i2", new BsonDocument("order", 2)));

            var items = WorkflowEngineService.PinnedOutputItems(context, Node("action", pin));

            items.Should().HaveCount(2);
            items.Select(i => i.Branch).Should().AllBe("source");
            items[0].Data.Output["charged"].AsInt32.Should().Be(1);
            items[1].ParentItemIds.Should().Equal("i2");
            items[1].Data.Input["order"].AsInt32.Should().Be(2);
        }

        [Fact]
        public void More_pin_entries_than_inputs_still_gives_every_entry()
        {
            var pin = new BsonArray { new BsonDocument("a", 1), new BsonDocument("a", 2) };

            var items = WorkflowEngineService.PinnedOutputItems(Context(), Node("action", pin));

            items.Should().HaveCount(2);
            items.Should().OnlyContain(i => i.ParentItemIds.Count == 0);
        }
    }
}
