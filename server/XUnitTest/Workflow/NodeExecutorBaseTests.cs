using DomainService.Workflow.Entities;
using DomainService.Workflow.Nodes;
using FluentAssertions;
using MongoDB.Bson;

namespace XUnitTest.Workflow
{
    public class NodeExecutorBaseTests
    {
        // Test-only concrete executor exposing the protected expression parser.
        private sealed class TestExecutor : NodeExecutorBase<TestParameters>
        {
            public override string NodeType => "test";
            public override string Version => "v1";

            public T? Parse<T>(string text, WorkflowItemExecutionEntity item, NodeExecutionContext ctx)
                => parseExpression<T>(text, item, ctx);

            public TestParameters? LastParameters { get; private set; }

            protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TestParameters? parameters)
            {
                LastParameters = parameters;
                return Task.FromResult(NodeExecutionResult.Empty());
            }
        }

        public sealed class TestParameters
        {
            public string Name { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        private static WorkflowItemExecutionEntity Item(BsonDocument output, Dictionary<string, string>? ancestors = null)
            => new()
            {
                Id = "item-1",
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                NodeId = "node-1",
                NodeExecutionId = "ne-1",
                NodeName = "Node1",
                Branch = "source",
                AncestorMap = ancestors ?? new Dictionary<string, string>(),
                Data = new NodeOutputItemData { Output = output }
            };

        private static NodeExecutionContext Context(
            IReadOnlyList<WorkflowItemExecutionEntity> items,
            BsonDocument? workflowContext = null,
            IReadOnlyDictionary<string, List<WorkflowItemExecutionEntity>>? ancestorOutputs = null)
            => new()
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument(),
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = workflowContext ?? new BsonDocument(),
                AncestorNodeOutputs = ancestorOutputs ?? new Dictionary<string, List<WorkflowItemExecutionEntity>>()
            };

        [Fact]
        public void Parse_EmptyText_ReturnsDefault()
        {
            var exec = new TestExecutor();
            var item = Item(new BsonDocument("name", "abc"));
            var ctx = Context(new[] { item });

            exec.Parse<string>("", item, ctx).Should().BeNull();
            exec.Parse<double>("", item, ctx).Should().Be(0);
        }

        [Fact]
        public void Parse_PlainText_PassesThroughForString()
        {
            var exec = new TestExecutor();
            var item = Item(new BsonDocument("name", "abc"));
            var ctx = Context(new[] { item });

            exec.Parse<string>("hello world", item, ctx).Should().Be("hello world");
        }

        [Fact]
        public void Parse_ContextExpression_ResolvesValue()
        {
            var exec = new TestExecutor();
            var item = Item(new BsonDocument("name", "abc"));
            var ctx = Context(new[] { item }, workflowContext: new BsonDocument("region", "eu"));

            exec.Parse<string>("{{$context.region}}", item, ctx).Should().Be("eu");
        }

        [Fact]
        public void Parse_ContextExpression_MissingKey_ReturnsEmpty()
        {
            var exec = new TestExecutor();
            var item = Item(new BsonDocument("name", "abc"));
            var ctx = Context(new[] { item }, workflowContext: new BsonDocument());

            exec.Parse<string>("{{$context.missing}}", item, ctx).Should().BeEmpty();
        }

        [Fact]
        public void Parse_JsonWholeOutput_ReturnsJson()
        {
            var exec = new TestExecutor();
            var item = Item(new BsonDocument { { "name", "abc" }, { "count", 3 } });
            var ctx = Context(new[] { item });

            var result = exec.Parse<string>("{{$json}}", item, ctx);
            result.Should().Contain("abc");
            result.Should().Contain("count");
        }

        [Fact]
        public void Parse_NodeReference_ResolvesAncestorOutput()
        {
            var exec = new TestExecutor();
            var ancestorItem = Item(new BsonDocument("greeting", "hi"));
            var ancestorId = ancestorItem.Id;

            var inputItem = Item(new BsonDocument("name", "abc"),
                ancestors: new Dictionary<string, string> { { "Prev", ancestorId } });

            var ancestorOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>
            {
                { "Prev", new List<WorkflowItemExecutionEntity> { ancestorItem } }
            };
            var ctx = Context(new[] { inputItem }, ancestorOutputs: ancestorOutputs);

            exec.Parse<string>("{{$node[\"Prev\"].json.output.greeting}}", inputItem, ctx)
                .Should().Be("hi");
        }

        [Fact]
        public void Parse_NodeReference_MissingAncestor_ReturnsEmpty()
        {
            var exec = new TestExecutor();
            var inputItem = Item(new BsonDocument("name", "abc"));
            var ctx = Context(new[] { inputItem });

            exec.Parse<string>("{{$node[\"Prev\"].json.output.greeting}}", inputItem, ctx)
                .Should().BeEmpty();
        }

        [Fact]
        public void Parse_TypedDouble_ParsesNumber()
        {
            var exec = new TestExecutor();
            var item = Item(new BsonDocument("name", "abc"));
            var ctx = Context(new[] { item });

            exec.Parse<double>("42.5", item, ctx).Should().Be(42.5);
        }

        [Fact]
        public void Parse_TypedBool_ParsesBoolean()
        {
            var exec = new TestExecutor();
            var item = Item(new BsonDocument("name", "abc"));
            var ctx = Context(new[] { item });

            exec.Parse<bool>("true", item, ctx).Should().BeTrue();
        }

        [Fact]
        public async Task RunAsync_DeserializesParametersAndInvokesExecute()
        {
            var exec = new TestExecutor();
            var item = Item(new BsonDocument("name", "abc"));
            var ctx = Context(new[] { item });
            ctx.Parameters = new BsonDocument { { "Name", "wf" }, { "Count", 7 } };

            var result = await exec.RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            exec.LastParameters.Should().NotBeNull();
            exec.LastParameters!.Name.Should().Be("wf");
            exec.LastParameters.Count.Should().Be(7);
        }
    }
}
