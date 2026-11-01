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
using Workflow.DomainService.Nodes.LogicFilterV2;
using Workflow.DomainService.Nodes.LogicIFV1;
using Workflow.DomainService.Nodes.LogicSwitchV2;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC9 Filter node: examples E1–E13, the log line, conditionType binding and equivalence with If and Switch.</summary>
    public class LogicFilterV2NodeTests
    {
        // The stage text is a log contract, so it is spelled out rather than taken from the constant.
        private const string KeptStage = "[wf:node.filter.applied] [node:f1#1] ";

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = new();
            public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        }

        private sealed record Run(NodeExecutionResult Result, List<LogEvent> Events, IReadOnlyList<WorkflowItemExecutionEntity> Inputs, BsonDocument Parameters)
        {
            public List<string> Lines => Events.Select(e => e.RenderMessage()).ToList();

            /// <summary>The node.filter.applied lines, without the stage and node prefix.</summary>
            public List<string> KeptLines => Lines.Where(l => l.StartsWith(KeptStage, StringComparison.Ordinal)).Select(l => l[KeptStage.Length..]).ToList();
        }

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
            Data = new NodeOutputItemData { Input = new BsonDocument("from", id), Output = output },
        };

        private static BsonDocument Cond(string left, string op, string? right, string type)
        {
            var doc = new BsonDocument { { "left", left }, { "operator", op }, { "type", type } };
            if (right != null) doc["right"] = right;
            return doc;
        }

        private static BsonDocument Params(BsonValue? conditionType, params BsonDocument[] conditions)
        {
            var doc = new BsonDocument { { "conditions", new BsonArray(conditions) } };
            if (conditionType != null) doc["conditionType"] = conditionType;
            return doc;
        }

        private static readonly BsonDocument Paid = Cond("{{ $json.status }}", "equals", "paid", "string");
        private static readonly BsonDocument Over100 = Cond("{{ $json.total }}", "greater_than", "100", "number");

        /// <summary>The §7 input: [{id 1, paid, 120}, {id 2, draft, 300}, {id 3, paid, 40}].</summary>
        private static List<WorkflowItemExecutionEntity> E1Items() =>
        [
            Item("item-0", BsonDocument.Parse("{ \"id\": 1, \"status\": \"paid\", \"total\": 120 }")),
            Item("item-1", BsonDocument.Parse("{ \"id\": 2, \"status\": \"draft\", \"total\": 300 }")),
            Item("item-2", BsonDocument.Parse("{ \"id\": 3, \"status\": \"paid\", \"total\": 40 }")),
        ];

        private static async Task<Run> RunFilter(
            BsonDocument parameters,
            List<WorkflowItemExecutionEntity>? items = null,
            IReadOnlyDictionary<string, List<WorkflowItemExecutionEntity>>? ancestors = null)
        {
            items ??= E1Items();
            var sink = new CollectingSink();
            using var factory = new SerilogLoggerFactory(
                new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger(),
                dispose: true);
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1",
                TenantId = "tenant-1",
                WorkflowId = "wf-1",
                WorkflowName = "wf",
                WorkflowSnapshot = new WorkflowEntity { ItemId = "wf-1", Name = "wf", TenantId = "tenant-1" },
                TriggerMetadata = new TriggerMetadata(),
                TraceId = "0af7651916cd43dd8448eb211c80319c",
            };
            var context = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
                AncestorNodeOutputs = ancestors ?? new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
                Log = new WorkflowExecutionLogger(factory.CreateLogger<WorkflowExecutionLogger>()).For(execution).ForNode("f1", 1),
            };
            var result = await new LogicFilterV2Node().RunAsync(context);
            return new Run(result, sink.Events, items, parameters);
        }

        private static List<int> KeptIds(Run run) =>
            run.Result.OutputItems.Where(o => !o.IsError).Select(o => o.Data.Output["id"].AsInt32).ToList();

        // ---------- E1 / E2: AND and OR (H6, H7, H8) ----------

        [Fact]
        public async Task E1_And_KeepsOnlyTheMatchingItem_Unchanged_WithItsInputAsParent()
        {
            var run = await RunFilter(Params("and", Paid, Over100));

            run.Result.IsSuccess.Should().BeTrue(run.Result.ErrorMessage);
            var kept = run.Result.OutputItems.Should().ContainSingle().Subject;
            kept.Branch.Should().Be("source");
            kept.IsError.Should().BeFalse();
            kept.ParentItemIds.Should().Equal("item-0");
            kept.Data.Output.Should().BeSameAs(run.Inputs[0].Data.Output);
            kept.Data.Input.Should().BeSameAs(run.Inputs[0].Data.Input);
            run.KeptLines.Should().Equal("Kept 1 of 3 item(s).");
        }

        [Fact]
        public async Task E2_Or_KeepsAllThreeItems_InInputOrder()
        {
            var run = await RunFilter(Params("or", Paid, Over100));

            run.Result.IsSuccess.Should().BeTrue(run.Result.ErrorMessage);
            KeptIds(run).Should().Equal(1, 2, 3);
            run.Result.OutputItems.Select(o => o.ParentItemIds!.Single()).Should().Equal("item-0", "item-1", "item-2");
            run.KeptLines.Should().Equal("Kept 3 of 3 item(s).");
        }

        [Fact]
        public async Task KeptItems_CarryTheParametersAsSaved()
        {
            var run = await RunFilter(Params("and", Paid, Over100));

            // A1: the saved camelCase document, not a PascalCase re-serialization of the typed parameters.
            run.Result.OutputItems.Single().Data.Parameters.Should().BeSameAs(run.Parameters);
        }

        [Fact]
        public async Task TheKeptLine_IsOneInfoLine_WithCountsOnly()
        {
            var run = await RunFilter(Params("and", Paid, Cond("{{ $json.total }}", "greater_than", "zz-marker-4711", "number")));

            var line = run.Events.Where(e => e.RenderMessage().StartsWith(KeptStage, StringComparison.Ordinal)).Should().ContainSingle().Subject;
            line.Level.Should().Be(LogEventLevel.Information);
            // C16: no condition operands, expressions or item values on any line the node writes.
            run.Lines.Should().NotContain(l => l.Contains("paid") || l.Contains("zz-marker-4711") || l.Contains("$json") || l.Contains("120"));
        }

        // ---------- E3 / E5: no match is success with 0 items (C1, C5, C6) ----------

        [Fact]
        public async Task E3_ComparisonIsCaseSensitive_NothingKept_Succeeds()
        {
            var run = await RunFilter(Params("and", Cond("{{ $json.status }}", "equals", "Paid", "string")));

            run.Result.IsSuccess.Should().BeTrue();
            run.Result.OutputItems.Should().BeEmpty();
            run.KeptLines.Should().Equal("Kept 0 of 3 item(s).");
        }

        [Fact]
        public async Task E5_UnreadableNumber_IsFalse_NotAnError()
        {
            var run = await RunFilter(Params("and", Cond("{{ $json.total }}", "greater_than", "abc", "number")));

            run.Result.IsSuccess.Should().BeTrue();
            run.Result.OutputItems.Should().BeEmpty();
            run.KeptLines.Should().Equal("Kept 0 of 3 item(s).");
        }

        // ---------- E6–E9: empty conditions, the blank default, no input (C7, C13) ----------

        [Theory]
        [InlineData("and", 3)]
        [InlineData("or", 0)]
        public async Task E6_E7_NoConditions_AndKeepsAll_OrKeepsNone(string conditionType, int kept)
        {
            var run = await RunFilter(Params(conditionType));

            run.Result.IsSuccess.Should().BeTrue();
            run.Result.OutputItems.Should().HaveCount(kept);
            run.KeptLines.Should().Equal($"Kept {kept} of 3 item(s).");
        }

        [Fact]
        public async Task E8_TheBlankDefaultCondition_KeepsEveryItem()
        {
            var run = await RunFilter(Params("and", Cond("", "equals", "", "string")));

            run.Result.IsSuccess.Should().BeTrue();
            KeptIds(run).Should().Equal(1, 2, 3);
            run.KeptLines.Should().Equal("Kept 3 of 3 item(s).");
        }

        [Fact]
        public async Task E9_NoInputItems_SucceedsWithNoItems()
        {
            var run = await RunFilter(Params("and", Paid, Over100), items: []);

            run.Result.IsSuccess.Should().BeTrue();
            run.Result.OutputItems.Should().BeEmpty();
            run.KeptLines.Should().Equal("Kept 0 of 0 item(s).");
        }

        // ---------- E4 / E10 and bad conditions: item errors fail the step (C3, C4) ----------

        [Fact]
        public async Task E4_UnknownOperator_EveryItemIsAnErrorItem_AndTheStepFails()
        {
            var run = await RunFilter(Params("and", Cond("{{ $json.status }}", "starts_with", "p", "string")));

            run.Result.IsSuccess.Should().BeFalse();
            run.Result.ErrorMessage.Should().Be("Condition could not be evaluated for 3 item(s).");
            run.Result.OutputItems.Should().HaveCount(3).And.OnlyContain(o => o.IsError && o.Branch == "source");
            run.Result.OutputItems.Select(o => o.Data.Output).Should().AllSatisfy(output =>
                output.Should().Be(new BsonDocument { { "error", true }, { "message", "Unknown operator starts_with for type string" } }));
            run.Result.OutputItems.Select(o => o.ParentItemIds!.Single()).Should().Equal("item-0", "item-1", "item-2");
            run.KeptLines.Should().Equal("Kept 0 of 3 item(s).");
        }

        [Fact]
        public async Task E10_AnExpressionThatThrowsForOneItem_FailsOnlyThatItem_AndKeepsTheMatches()
        {
            // $node["Limits"] needs the item's lineage; the second item's lineage map is missing, so reading
            // the reference throws for that item only. The others compare total with Limits' max (100).
            var limits = Item("lim-0", new BsonDocument("max", 100));
            var items = E1Items();
            items[0].AncestorMap = new Dictionary<string, string> { ["Limits"] = "lim-0" };
            items[1].AncestorMap = null!;
            items[2].AncestorMap = new Dictionary<string, string> { ["Limits"] = "lim-0" };
            var condition = Cond("{{ $json.total }}", "greater_than", "{{ $node[\"Limits\"].json.output.max }}", "number");

            var run = await RunFilter(Params("and", condition), items, new Dictionary<string, List<WorkflowItemExecutionEntity>> { ["Limits"] = [limits] });

            run.Result.IsSuccess.Should().BeFalse();
            run.Result.ErrorMessage.Should().Be("Condition could not be evaluated for 1 item(s).");
            run.Result.OutputItems.Should().HaveCount(2);
            KeptIds(run).Should().Equal(1);
            run.Result.OutputItems.Single(o => o.IsError).ParentItemIds.Should().Equal("item-1");
            run.KeptLines.Should().Equal("Kept 1 of 3 item(s).");
        }

        [Fact]
        public async Task ANullCondition_IsAnItemError()
        {
            var parameters = new BsonDocument { { "conditionType", "and" }, { "conditions", new BsonArray { BsonNull.Value } } };

            var run = await RunFilter(parameters, [E1Items()[0]]);

            run.Result.IsSuccess.Should().BeFalse();
            run.Result.ErrorMessage.Should().Be("Condition could not be evaluated for 1 item(s).");
            run.Result.OutputItems.Should().ContainSingle().Which.Data.Output["message"].AsString.Should().Be("Condition is empty");
        }

        [Theory]
        [InlineData("and", "nope")] // the first condition is false, which already decides AND
        [InlineData("or", "paid")] // the first condition is true, which already decides OR
        public async Task AnUnknownType_IsAnItemError_EvenWhenAnEarlierConditionAlreadyDecides(string conditionType, string status)
        {
            var first = Cond("{{ $json.status }}", "equals", status, "string");

            var run = await RunFilter(Params(conditionType, first, Cond("a", "equals", "a", "Money")), [E1Items()[0]]);

            run.Result.IsSuccess.Should().BeFalse();
            run.Result.ErrorMessage.Should().Be("Condition could not be evaluated for 1 item(s).");
            run.Result.OutputItems.Should().ContainSingle().Which.Data.Output["message"].AsString.Should().Be("Unknown type money");
        }

        // ---------- E11–E13: conditionType binding (C8) ----------

        public static IEnumerable<object?[]> BindingConditionTypes()
        {
            yield return ["and", new[] { 1 }];
            yield return ["And", new[] { 1 }];
            yield return ["or", new[] { 1, 2, 3 }];
            yield return ["OR", new[] { 1, 2, 3 }];
            yield return [0, new[] { 1 }];
            // Q2 (answered A): the converter also accepts these; pinned so a change is deliberate.
            yield return ["0", new[] { 1 }];
            yield return ["1", new[] { 1, 2, 3 }];
            yield return [" and", new[] { 1 }];
        }

        [Theory]
        [MemberData(nameof(BindingConditionTypes))]
        public async Task ConditionType_TextAnyCase_And0Or1_Bind(object conditionType, int[] kept)
        {
            var run = await RunFilter(Params(BsonValue.Create(conditionType), Paid, Over100));

            run.Result.IsSuccess.Should().BeTrue(run.Result.ErrorMessage);
            KeptIds(run).Should().Equal(kept);
            run.KeptLines.Should().Equal($"Kept {kept.Length} of 3 item(s).");
        }

        [Fact]
        public async Task E12_ConditionTypeMissingOrNull_MeansAnd()
        {
            foreach (var parameters in new[] { Params(null, Paid, Over100), Params(BsonNull.Value, Paid, Over100) })
            {
                var run = await RunFilter(parameters);

                run.Result.IsSuccess.Should().BeTrue(run.Result.ErrorMessage);
                KeptIds(run).Should().Equal(1);
                run.KeptLines.Should().Equal("Kept 1 of 3 item(s).");
            }
        }

        [Fact]
        public async Task E13_ConditionTypeOne_MeansOr()
        {
            var run = await RunFilter(Params(1, Paid, Over100));

            KeptIds(run).Should().Equal(1, 2, 3);
            run.KeptLines.Should().Equal("Kept 3 of 3 item(s).");
        }

        public static IEnumerable<object[]> FailingConditionTypes()
        {
            yield return ["all"];
            yield return [""];
            yield return ["any"];
            yield return [2];
            yield return [-1];
            yield return ["2"];
            yield return [1.0];
            yield return [true];
        }

        [Theory]
        [MemberData(nameof(FailingConditionTypes))]
        public async Task E11_AnyOtherConditionType_FailsBeforeAnyItem_NamingConditionType(object conditionType)
        {
            var run = await RunFilter(Params(BsonValue.Create(conditionType), Paid, Over100));

            run.Result.IsSuccess.Should().BeFalse();
            run.Result.ErrorMessage.Should().Be("Parameter 'conditionType' has the wrong shape and could not be read.");
            (run.Result.OutputItems ?? []).Should().BeEmpty();
            run.KeptLines.Should().BeEmpty();
        }

        // ---------- H10: the same condition means the same in If, Switch and Filter ----------

        public static IEnumerable<object?[]> EquivalenceConditions()
        {
            yield return ["{{ $json.status }}", "equals", "paid", "string"];
            yield return ["{{ $json.status }}", "not_equals", "paid", "string"];
            yield return ["{{ $json.status }}", "contains", "ai", "string"];
            yield return ["{{ $json.status }}", "in", "a, paid ,c", "string"];
            yield return ["{{ $json.status }}", "equals", "Paid", "string"];
            yield return ["{{ $json.total }}", "greater_than", "100", "number"];
            yield return ["{{ $json.total }}", "less_or_equal", "120", "number"];
            yield return ["{{ $json.total }}", "less_than", "abc", "number"];
            yield return ["{{ $json.status }}", "equals", "paid", null];
            yield return ["true", "is_true", null, "boolean"];
            yield return ["2026-01-02", "greater_than", "2026-01-01", "date_time"];
            yield return ["[\"a\",\"b\"]", "in", "[\"a\",\"b\",\"c\"]", "array"];
            yield return ["", "equals", "", "string"];
        }

        [Theory]
        [MemberData(nameof(EquivalenceConditions))]
        public async Task KeepsAnItemExactlyWhenIfIsTrue_AndSwitchRoutesIt(string left, string op, string? right, string? type)
        {
            var condition = Cond(left, op, right, type ?? "");
            if (type is null) condition.Remove("type");
            foreach (var input in E1Items())
            {
                foreach (var (filterType, switchType) in new[] { ("and", "all"), ("or", "or") })
                {
                    var because = $"{op} {type} {filterType} on {input.Id}";
                    var context = (BsonDocument parameters) => new NodeExecutionContext
                    {
                        WorkflowExecutionId = "exec-1",
                        TenantId = "tenant-1",
                        Parameters = parameters,
                        InputItems = [input],
                        IterationCount = 1,
                        WorkflowContext = new BsonDocument(),
                    };

                    var ifResult = await new LogicIfV1Node().RunAsync(context(Params(filterType, condition)));
                    var switchResult = await new LogicSwitchV2Node().RunAsync(context(new BsonDocument
                    {
                        { "mode", "rules" },
                        { "rules", new BsonArray { new BsonDocument { { "handleKey", "switch-handle-source-aaaaa1" }, { "conditionType", switchType }, { "conditions", new BsonArray { condition } } } } },
                        { "options", new BsonDocument("fallbackOutput", "none") },
                    }));
                    var filterResult = await new LogicFilterV2Node().RunAsync(context(Params(filterType, condition)));

                    var ifTrue = ifResult.OutputItems.Single().Branch == "if-true";
                    filterResult.IsSuccess.Should().BeTrue(because);
                    filterResult.OutputItems.Should().OnlyContain(o => !o.IsError, because);
                    filterResult.OutputItems.Any().Should().Be(ifTrue, because);
                    switchResult.OutputItems.Any().Should().Be(ifTrue, because);
                }
            }
        }

        // ---------- versions ----------

        [Fact]
        public void DeclaresTypeAndVersion()
        {
            INodeExecutor executor = new LogicFilterV2Node();
            executor.NodeType.Should().Be("filter");
            executor.Version.Should().Be("v2");
        }
    }

    /// <summary>SPEC9 C9: Api and Worker both call AddWorkflowExecutionEngine, which registers (filter, v2) once.</summary>
    public class LogicFilterV2RegistrationTests
    {
        [Fact]
        public void AddWorkflowExecutionEngine_RegistersTheFilterExecutorOnce()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            global::Workflow.DomainService.WorkflowExecutionServiceCollectionExtensions.AddWorkflowExecutionEngine(services);

            services.Where(d => d.ServiceType == typeof(INodeExecutor)).Select(d => d.ImplementationType)
                .Should().ContainSingle(t => t == typeof(LogicFilterV2Node));
        }
    }
}
