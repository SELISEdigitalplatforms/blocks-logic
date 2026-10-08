using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.LogicIFV1;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// If node: "in" / "not_in" are evaluated (they used to fall to `_ => true`), an unknown operator or
    /// type is an error, and an item that cannot be evaluated fails the step (it used to become an error
    /// item on branch "source", which an If node has no edge for, so it was silently dropped).
    /// </summary>
    public class LogicIfV1NodeTests
    {
        private static WorkflowItemExecutionEntity Item(string id, BsonDocument output) => new()
        {
            Id = id,
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            NodeId = "trigger",
            NodeExecutionId = "ne-1",
            NodeName = "Trigger",
            Branch = "source",
            ParentItemIds = new List<string>(),
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = output },
        };

        private static BsonDocument Cond(string left, string op, string? right, string? type)
        {
            var doc = new BsonDocument { { "left", left }, { "operator", op } };
            if (right != null) doc["right"] = right;
            if (type != null) doc["type"] = type;
            return doc;
        }

        private static Task<NodeExecutionResult> Run(BsonDocument output, params BsonDocument[] conditions)
            => Run(new[] { output }, "and", conditions);

        private static Task<NodeExecutionResult> Run(BsonDocument[] outputs, string conditionType, params BsonDocument[] conditions)
        {
            var items = outputs.Select((o, i) => Item($"item-{i}", o)).ToList();
            var context = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument
                {
                    { "conditionType", conditionType },
                    { "conditions", new BsonArray(conditions) },
                },
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
            };
            return new LogicIfV1Node().RunAsync(context);
        }

        private static string Branch(NodeExecutionResult result)
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            return result.OutputItems.Should().ContainSingle().Subject.Branch;
        }

        // ---------- string in / not_in ----------

        [Theory]
        [InlineData("b", "[\"a\",\"b\",\"c\"]", "if-true")]   // JSON array literal
        [InlineData("b", "a, b ,c", "if-true")]              // comma-separated, spaces trimmed
        [InlineData("d", "a,b,c", "if-false")]
        [InlineData("B", "a,b,c", "if-false")]               // case-sensitive, like equals
        [InlineData("a", "a", "if-true")]                    // one-value list
        [InlineData("a", "", "if-false")]                    // empty list
        [InlineData("", "", "if-false")]                     // empty right text is an empty list
        [InlineData("", "[\"\"]", "if-true")]                // an explicit empty-string entry
        public async Task String_in(string left, string right, string expected)
        {
            var result = await Run(new BsonDocument(), Cond(left, "in", right, "string"));
            Branch(result).Should().Be(expected);
        }

        [Theory]
        [InlineData("b", "a,b,c", "if-false")]
        [InlineData("d", "a,b,c", "if-true")]
        [InlineData("x", "[\"a\"]", "if-true")]
        public async Task String_not_in(string left, string right, string expected)
        {
            var result = await Run(new BsonDocument(), Cond(left, "not_in", right, "string"));
            Branch(result).Should().Be(expected);
        }

        [Fact]
        public async Task String_in_resolves_expressions_on_both_sides()
        {
            var output = new BsonDocument
            {
                { "status", "open" },
                { "allowed", new BsonArray { "open", "pending" } },
            };

            var result = await Run(output, Cond("{{$json.output.status}}", "in", "{{$json.output.allowed}}", "string"));

            Branch(result).Should().Be("if-true");
        }

        [Fact]
        public async Task Missing_type_defaults_to_string()
        {
            var result = await Run(new BsonDocument(), Cond("b", "in", "a,b", null));
            Branch(result).Should().Be("if-true");
        }

        // ---------- array in / not_in (left ⊆ right) ----------

        [Theory]
        [InlineData("[\"a\",\"b\"]", "[\"a\",\"b\",\"c\"]", "if-true")]   // every left value in right
        [InlineData("[\"a\",\"x\"]", "[\"a\",\"b\",\"c\"]", "if-false")]  // one missing
        [InlineData("a,b", "a,b,c", "if-true")]                          // comma-separated both sides
        [InlineData("[]", "[\"a\"]", "if-false")]                         // empty left is not "in"
        [InlineData("[\"a\"]", "[]", "if-false")]
        public async Task Array_in_is_subset(string left, string right, string expected)
        {
            var result = await Run(new BsonDocument(), Cond(left, "in", right, "array"));
            Branch(result).Should().Be(expected);
        }

        [Theory]
        [InlineData("[\"a\",\"b\"]", "[\"a\",\"b\",\"c\"]", "if-false")]
        [InlineData("[\"a\",\"x\"]", "[\"a\",\"b\",\"c\"]", "if-true")]
        [InlineData("[]", "[\"a\"]", "if-true")]
        public async Task Array_not_in_is_negation_of_in(string left, string right, string expected)
        {
            var result = await Run(new BsonDocument(), Cond(left, "not_in", right, "array"));
            Branch(result).Should().Be(expected);
        }

        [Fact]
        public async Task Array_contains_is_unchanged_shares_any_value()
        {
            var result = await Run(new BsonDocument(), Cond("[\"a\",\"x\"]", "contains", "[\"a\",\"b\"]", "array"));
            Branch(result).Should().Be("if-true");
        }

        // ---------- existing operators still work ----------

        [Theory]
        [InlineData("5", "greater_than", "3", "number", "if-true")]
        [InlineData("abc", "greater_than", "3", "number", "if-false")]  // not a number: false, not an error
        [InlineData("true", "is_true", null, "boolean", "if-true")]
        [InlineData("true", "not_equals", "false", "boolean", "if-true")]
        [InlineData("2026-01-02", "greater_than", "2026-01-01", "date_time", "if-true")]
        [InlineData("hello", "contains", "ell", "string", "if-true")]
        public async Task Known_operators_unchanged(string left, string op, string? right, string type, string expected)
        {
            var result = await Run(new BsonDocument(), Cond(left, op, right, type));
            Branch(result).Should().Be(expected);
        }

        // ---------- unknown operator / type fail the step ----------

        [Theory]
        [InlineData("string", "greater_than")]
        [InlineData("string", "")]
        [InlineData("number", "in")]
        [InlineData("number", "contains")]
        [InlineData("boolean", "greater_than")]
        [InlineData("date_time", "in")]
        [InlineData("array", "equals")]
        public async Task Unknown_operator_fails_the_step(string type, string op)
        {
            var result = await Run(new BsonDocument(), Cond("1", op, "1", type));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Item 0").And.Contain("Unknown operator");
            var item = result.OutputItems.Should().ContainSingle().Subject;
            item.Data.Output["error"].AsBoolean.Should().BeTrue();
            item.Branch.Should().NotBe("if-true").And.NotBe("if-false");
        }

        [Fact]
        public async Task Unknown_type_fails_the_step()
        {
            var result = await Run(new BsonDocument(), Cond("1", "equals", "1", "uuid"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Item 0").And.Contain("Unknown type uuid");
        }

        [Fact]
        public async Task Unknown_operator_fails_even_when_an_earlier_condition_decides_the_result()
        {
            // AND with a false first condition used to short-circuit, hiding the bad second one.
            var result = await Run(new BsonDocument(),
                Cond("a", "equals", "b", "string"),
                Cond("a", "bogus", "a", "string"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Unknown operator bogus");
        }

        [Fact]
        public async Task Or_with_unknown_operator_fails_even_when_another_condition_is_true()
        {
            var result = await Run(new[] { new BsonDocument() }, "or",
                Cond("a", "equals", "a", "string"),
                Cond("a", "bogus", "a", "string"));

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task Null_condition_fails_the_step()
        {
            var context = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument
                {
                    { "conditionType", "and" },
                    { "conditions", new BsonArray { BsonNull.Value } },
                },
                InputItems = new List<WorkflowItemExecutionEntity> { Item("item-0", new BsonDocument()) },
                IterationCount = 1,
                WorkflowContext = new BsonDocument(),
            };

            var result = await new LogicIfV1Node().RunAsync(context);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Item 0");
        }

        [Fact]
        public async Task Failure_counts_bad_items_and_names_the_first()
        {
            var outputs = new[] { new BsonDocument(), new BsonDocument(), new BsonDocument() };

            var result = await Run(outputs, "and", Cond("a", "nope", "a", "string"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("3 item(s)").And.Contain("First: Item 0").And.Contain("Unknown operator nope");
            result.OutputItems.Should().HaveCount(3).And.OnlyContain(i => i.Data.Output["error"].AsBoolean);
        }

        [Fact]
        public async Task No_items_succeeds_with_no_output()
        {
            var result = await Run(Array.Empty<BsonDocument>(), "and", Cond("a", "bogus", "a", "string"));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
        }
    }
}
