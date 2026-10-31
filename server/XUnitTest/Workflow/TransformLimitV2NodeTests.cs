using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.TransformLimitV2;
using Workflow.DomainService.Services;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC10 Limit: keeps at most Max Items from the start or the end, in input order, lineage kept.</summary>
    public class TransformLimitV2NodeTests
    {
        private const string OutOfRange = "Max Items must be 1 or more.";
        private const string KeepShape = "Parameter 'keep' has the wrong shape and could not be read.";

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = new();
            public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        }

        private static WorkflowItemExecutionEntity Item(string id, BsonValue output) => new()
        {
            Id = id,
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            NodeId = "parent",
            NodeExecutionId = "ne-1",
            NodeName = "Parent",
            Branch = "source",
            ParentItemIds = new List<string>(),
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Input = new BsonDocument("from", id), Output = output },
        };

        private static List<WorkflowItemExecutionEntity> Items(int count) =>
            Enumerable.Range(1, count).Select(i => Item($"x{i}", new BsonDocument("id", i))).ToList();

        private static async Task<(NodeExecutionResult Result, List<string> Lines)> RunLogged(string parametersJson, List<WorkflowItemExecutionEntity> items)
        {
            var sink = new CollectingSink();
            using var factory = new SerilogLoggerFactory(new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(sink).CreateLogger(), dispose: true);
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1",
                TenantId = "tenant-1",
                WorkflowId = "wf-1",
                WorkflowName = "Limit",
                WorkflowSnapshot = new WorkflowEntity { ItemId = "wf-1", Name = "Limit", TenantId = "tenant-1" },
                TriggerMetadata = new TriggerMetadata(),
                TraceId = "0af7651916cd43dd8448eb211c80319c",
            };
            var log = new WorkflowExecutionLogger(factory.CreateLogger<WorkflowExecutionLogger>()).For(execution).ForNode("limit-1", 1);

            var result = await new TransformLimitV2Node().RunAsync(new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = BsonDocument.Parse(parametersJson),
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
                Log = log,
            });
            return (result, sink.Events.Select(e => e.RenderMessage()).ToList());
        }

        private static async Task<NodeExecutionResult> Run(string parametersJson, int count = 5) =>
            (await RunLogged(parametersJson, Items(count))).Result;

        private static List<int> Ids(NodeExecutionResult result)
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            return result.OutputItems.Select(o => o.Data.Output.AsBsonDocument["id"].AsInt32).ToList();
        }

        private static void ShouldFail(NodeExecutionResult result, string message)
        {
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be(message);
            (result.OutputItems ?? new List<NodeOutputItem>()).Should().BeEmpty();
        }

        [Fact]
        public async Task E1_First_KeepsTheFirstItems_WithLineageAndUnchangedData()
        {
            var items = Items(5);
            var (result, lines) = await RunLogged("{ \"maxItems\": 2, \"keep\": \"first\" }", items);

            Ids(result).Should().Equal(1, 2);
            for (var i = 0; i < 2; i++)
            {
                var output = result.OutputItems[i];
                output.Branch.Should().Be("source");
                output.ParentItemIds.Should().Equal(items[i].Id);
                output.Data.Output.Should().BeSameAs(items[i].Data.Output);
                output.Data.Input.Should().BeSameAs(items[i].Data.Input);
                output.Data.Parameters.AsBsonDocument["keep"].AsString.Should().Be("first");
            }
            lines.Should().Contain("[wf:node.limit.applied] [node:limit-1#1] Kept 2 of 5 item(s).");
        }

        [Fact]
        public async Task E2_Last_KeepsTheLastItems_InInputOrder()
        {
            var (result, lines) = await RunLogged("{ \"maxItems\": 2, \"keep\": \"last\" }", Items(5));

            Ids(result).Should().Equal(4, 5);
            result.OutputItems.Select(o => o.ParentItemIds!.Single()).Should().Equal("x4", "x5");
            lines.Should().Contain(line => line.EndsWith("Kept 2 of 5 item(s).", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("{ \"maxItems\": 10, \"keep\": \"first\" }")]
        [InlineData("{ \"maxItems\": 5, \"keep\": \"last\" }")]
        [InlineData("{ \"maxItems\": 10, \"keep\": \"last\" }")]
        public async Task E3_E4_NoMoreItemsThanMax_KeepsAll(string parameters)
        {
            Ids(await Run(parameters)).Should().Equal(1, 2, 3, 4, 5);
        }

        [Fact]
        public async Task E5_NoInputItems_SucceedsEmpty()
        {
            var (result, lines) = await RunLogged("{ \"maxItems\": 3 }", new List<WorkflowItemExecutionEntity>());

            Ids(result).Should().BeEmpty();
            lines.Should().Contain(line => line.EndsWith("Kept 0 of 0 item(s).", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("{ \"maxItems\": 0 }")]
        [InlineData("{ \"maxItems\": -1 }")]
        [InlineData("{ \"maxItems\": \"0\" }")]
        public async Task E6_ZeroOrNegative_Fails_WithoutAKeptLine(string parameters)
        {
            var (result, lines) = await RunLogged(parameters, Items(5));

            ShouldFail(result, OutOfRange);
            lines.Should().NotContain(line => line.Contains("node.limit.applied", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("{ }")]
        [InlineData("{ \"maxItems\": null }")]
        [InlineData("{ \"keep\": \"first\" }")]
        public async Task MissingMaxItems_DefaultsTo1(string parameters)
        {
            Ids(await Run(parameters)).Should().Equal(1);
        }

        [Fact]
        public async Task NumericText_IsReadAsAnInteger()
        {
            Ids(await Run("{ \"maxItems\": \"2\" }")).Should().Equal(1, 2);
        }

        [Theory]
        [InlineData("{ \"maxItems\": 2.5 }")]
        [InlineData("{ \"maxItems\": \"abc\" }")]
        [InlineData("{ \"maxItems\": true }")]
        [InlineData("{ \"maxItems\": [3] }")]
        [InlineData("{ \"maxItems\": 2147483648 }")]
        public async Task NotAnInteger_FailsWithTheShapeMessage(string parameters)
        {
            var result = await Run(parameters);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().StartWith("Parameter 'maxItems' has the wrong shape");
            (result.OutputItems ?? new List<NodeOutputItem>()).Should().BeEmpty();
        }

        [Fact]
        public async Task E13_MaxInt_KeepsAll_AndAllocatesOnlyTheKeptItems()
        {
            var result = await Run("{ \"maxItems\": 2147483647 }");

            Ids(result).Should().Equal(1, 2, 3, 4, 5);
            result.OutputItems.Should().HaveCount(5);
        }

        [Theory]
        [InlineData("{ \"maxItems\": 2, \"keep\": \"middle\" }")]
        [InlineData("{ \"maxItems\": 2, \"keep\": \"\" }")]
        [InlineData("{ \"maxItems\": 2, \"keep\": 2 }")]
        [InlineData("{ \"maxItems\": 2, \"keep\": \"firstItems\" }")]
        public async Task E15_UnknownKeep_FailsWithTheShapeMessage(string parameters)
        {
            ShouldFail(await Run(parameters), KeepShape);
        }

        [Theory]
        [InlineData("{ \"maxItems\": 2 }", new[] { 1, 2 })]
        [InlineData("{ \"maxItems\": 2, \"keep\": null }", new[] { 1, 2 })]
        [InlineData("{ \"maxItems\": 2, \"keep\": 0 }", new[] { 1, 2 })]
        [InlineData("{ \"maxItems\": 2, \"keep\": 1 }", new[] { 4, 5 })]
        [InlineData("{ \"maxItems\": 2, \"keep\": \"Last\" }", new[] { 4, 5 })]
        public async Task E16_E17_KeepDefaultsToFirst_AndAcceptsNumbersAndAnyCase(string parameters, int[] expected)
        {
            Ids(await Run(parameters)).Should().Equal(expected);
        }

        [Fact]
        public async Task E18_KeepIsCheckedBeforeMaxItems()
        {
            ShouldFail(await Run("{ \"maxItems\": 0, \"keep\": \"middle\" }"), KeepShape);
        }

        [Theory]
        [InlineData("{ \"maxItems\": -987654 }")]
        [InlineData("{ \"maxItems\": 2, \"keep\": \"secret-token-123\" }")]
        public async Task C3_FailureNeverEchoesTheSavedValue(string parameters)
        {
            var (result, lines) = await RunLogged(parameters, Items(5));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotContain("secret").And.NotContain("987654");
            lines.Should().NotContain(line => line.Contains("secret", StringComparison.Ordinal) || line.Contains("987654", StringComparison.Ordinal));
        }

        [Fact]
        public void NodeTypeAndVersion()
        {
            var node = new TransformLimitV2Node();
            node.NodeType.Should().Be("limit");
            node.Version.Should().Be("v2");
        }
    }
}
