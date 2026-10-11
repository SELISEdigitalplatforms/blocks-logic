using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Nodes;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// C-9 (user, 2026-10-08): a step with any failed item fails, so the run ends Failed — the webhook says
    /// Failed and Resume is offered. Items are kept. Only items the node marked as failed count; an API
    /// answer that merely contains "error": true is data.
    /// </summary>
    public class StepErrorFailsRunTests
    {
        private static NodeOutputItem Item(BsonDocument output, bool isError = false) => new()
        {
            Data = new NodeOutputItemData { Output = output },
            Branch = "source",
            IsError = isError,
        };

        [Fact]
        public void A_step_with_a_failed_item_fails_and_keeps_every_item()
        {
            var items = new List<NodeOutputItem>
            {
                Item(new BsonDocument("ok", 1)),
                Item(new BsonDocument { { "error", true }, { "message", "503 from upstream" } }, isError: true),
                Item(new BsonDocument("ok", 3)),
            };

            var result = NodeExecutorBase<object>.FailIfAnyItemFailed(NodeExecutionResult.Successful(items));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("1 of 3 item(s) failed. First: 503 from upstream");
            result.OutputItems.Should().HaveCount(3);
        }

        [Fact]
        public void A_single_failed_item_gives_its_own_message()
        {
            var result = NodeExecutorBase<object>.FailIfAnyItemFailed(NodeExecutionResult.Successful(
                [Item(new BsonDocument { { "error", true }, { "message", "no agent" } }, isError: true)]));

            result.ErrorMessage.Should().Be("no agent");
        }

        [Fact]
        public void An_answer_that_contains_error_true_is_data_not_a_failure()
        {
            var result = NodeExecutorBase<object>.FailIfAnyItemFailed(NodeExecutionResult.Successful(
                [Item(new BsonDocument { { "error", true }, { "message", "the API's own field" } })]));

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public void Error_items_are_marked_by_the_builder()
        {
            ErrorItems.Build("boom").IsError.Should().BeTrue();
        }

        private sealed class ErrorItems : NodeExecutorBase<object>
        {
            public override string NodeType => "x";
            public override string Version => "v1";
            protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, object? parameters) =>
                Task.FromResult(NodeExecutionResult.Empty());
            public static NodeOutputItem Build(string message) => TryBuildErrorOutputItem(null, new BsonDocument(), message)!;
        }
    }
}
