using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.TransformMergeV2;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC3 Merge (Append) executor.</summary>
    public class TransformMergeV2NodeTests
    {
        private static WorkflowItemExecutionEntity Item(string id, string nodeId, int index = 0) => new()
        {
            Id = id,
            WorkflowExecutionId = "exec-1",
            TenantId = "t1",
            NodeId = nodeId,
            NodeExecutionId = $"ne-{nodeId}",
            NodeName = nodeId,
            Branch = "source",
            ItemIndex = index,
            ParentItemIds = new List<string>(),
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = new BsonDocument("id", id) },
        };

        private static Task<NodeExecutionResult> Run(BsonDocument parameters, Dictionary<string, IReadOnlyList<WorkflowItemExecutionEntity>> byHandle)
        {
            var all = byHandle.Values.SelectMany(v => v).ToList();
            return new TransformMergeV2Node().RunAsync(new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "t1",
                Parameters = parameters,
                InputItems = all,
                IterationCount = all.Count,
                WorkflowContext = new BsonDocument(),
                InputItemsByHandle = byHandle,
            });
        }

        private static BsonDocument Params(BsonValue? numberInputs = null, string mode = "append")
        {
            var doc = new BsonDocument("mode", mode);
            if (numberInputs != null) doc["numberInputs"] = numberInputs;
            return doc;
        }

        private static List<string> Ids(NodeExecutionResult result)
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            return result.OutputItems.Select(o => o.Data.Output["id"].AsString).ToList();
        }

        [Fact]
        public async Task Example1_AppendsInInputOrder_NotEdgeOrder()
        {
            var result = await Run(Params(2), new()
            {
                ["merge-handle-target-2"] = [Item("b1", "p2")],
                ["merge-handle-target-1"] = [Item("a1", "p1", 0), Item("a2", "p1", 1)],
            });

            Ids(result).Should().Equal("a1", "a2", "b1");
            result.OutputItems.Select(o => o.ParentItemIds!.Single()).Should().Equal("a1", "a2", "b1");
            result.OutputItems.Should().OnlyContain(o => o.Branch == "source");
        }

        [Fact]
        public async Task OutputIsACopy_AndInputIsTheSourceOutput()
        {
            var source = Item("a1", "p1");
            var result = await Run(Params(1), new() { ["merge-handle-target-1"] = [source] });

            var item = result.OutputItems.Single();
            item.Data.Input.Should().BeSameAs(source.Data.Output);
            item.Data.Output.Should().Be(source.Data.Output);
            item.Data.Output.Should().NotBeSameAs(source.Data.Output);
        }

        [Theory]
        [InlineData(null, 2)]
        [InlineData(2.5, 2)]
        [InlineData(0.0, 1)]
        [InlineData(-3.0, 1)]
        [InlineData(5.0, 5)]
        public void C3_InputCount(double? value, double expected)
        {
            TransformMergeV2Node.InputCount(value).Should().Be(expected);
        }

        [Fact]
        public async Task C3_FractionalNumberInputs_BindsWithoutAShapeError()
        {
            var result = await Run(Params(2.5), new()
            {
                ["merge-handle-target-1"] = [Item("a1", "p1")],
                ["merge-handle-target-2"] = [Item("b1", "p2")],
                ["merge-handle-target-3"] = [Item("c1", "p3")],
            });

            Ids(result).Should().Equal("a1", "b1");
        }

        [Fact]
        public async Task C4_ItemsAboveNOrOnUnknownHandles_AreIgnored()
        {
            var result = await Run(Params(1), new()
            {
                ["merge-handle-target-1"] = [Item("x1", "p1")],
                ["merge-handle-target-2"] = [Item("y1", "p2")],
                ["input-1"] = [Item("z1", "p3")],
            });

            Ids(result).Should().Equal("x1");
        }

        [Fact]
        public async Task C5_BlankHandle_IsInput1_BeforeTheOtherInput1Items()
        {
            var result = await Run(Params(2), new()
            {
                ["merge-handle-target-1"] = [Item("a1", "p1")],
                [""] = [Item("blank", "p0")],
                ["merge-handle-target-2"] = [Item("b1", "p2")],
            });

            Ids(result).Should().Equal("blank", "a1", "b1");
        }

        [Theory]
        [InlineData("combine")]
        [InlineData("")]
        public async Task C7_UnsupportedMode_Fails(string mode)
        {
            var result = await Run(Params(2, mode), new());
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("Unsupported Merge mode.");
        }

        [Fact]
        public async Task HugeNumberInputs_IsBoundedByTheEdges()
        {
            var started = DateTime.UtcNow;
            var result = await Run(Params(1e12), new()
            {
                ["merge-handle-target-1"] = [Item("a1", "p1")],
                ["merge-handle-target-999999999999"] = [Item("z1", "p9")],
            });

            Ids(result).Should().Equal("a1", "z1");
            (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task NoInputs_SucceedsWithNoItems()
        {
            (await Run(Params(2), new())).OutputItems.Should().BeEmpty();
        }

        [Fact]
        public void C18_DeclaresOnlyV2_AndIsRegistered()
        {
            INodeExecutor executor = new TransformMergeV2Node();
            executor.NodeType.Should().Be("merge");
            executor.Versions.Should().Equal("v2");
            var registry = new NodeExecutorRegistry([executor]);
            registry.Resolve("merge", "v2").Executor.Should().BeSameAs(executor);
            var act = () => registry.Resolve("merge", "v1");
            act.Should().Throw<InvalidOperationException>();

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            global::Workflow.DomainService.WorkflowExecutionServiceCollectionExtensions.AddWorkflowExecutionEngine(services);
            services.Where(d => d.ServiceType == typeof(INodeExecutor)).Select(d => d.ImplementationType)
                .Should().ContainSingle(t => t == typeof(TransformMergeV2Node));
        }
    }

    public class NodeInputRuleTests
    {
        [Fact]
        public void Defaults_ForNullEmptyUnknownAndNonString()
        {
            NodeInputRule.Parse(null).Should().Be(NodeInputRule.Default);
            NodeInputRule.Parse(new BsonDocument()).Should().Be(NodeInputRule.Default);
            NodeInputRule.Parse(new BsonDocument { { "runWhen", "sometimes" }, { "ifInputMissing", "explode" } }).Should().Be(NodeInputRule.Default);
            NodeInputRule.Parse(new BsonDocument { { "runWhen", 1 }, { "ifInputMissing", true } }).Should().Be(NodeInputRule.Default);
            NodeInputRule.Parse(new BsonDocument("retry", 3)).Should().Be(NodeInputRule.Default);
        }

        [Fact]
        public void Parses_AllInputsAndFail()
        {
            NodeInputRule.Parse(new BsonDocument { { "runWhen", "allInputsHaveData" }, { "ifInputMissing", "fail" } })
                .Should().Be(new NodeInputRule(true, true));
            NodeInputRule.Parse(new BsonDocument { { "runWhen", "allInputsHaveData" } })
                .Should().Be(new NodeInputRule(true, false));
        }

        [Theory]
        [InlineData(false, false, 2, 1, InputRuleDecision.Run)]
        [InlineData(true, false, 2, 0, InputRuleDecision.Run)]
        [InlineData(true, false, 2, 1, InputRuleDecision.Skip)]
        [InlineData(true, true, 2, 1, InputRuleDecision.Fail)]
        [InlineData(true, true, 0, 0, InputRuleDecision.Run)]
        [InlineData(false, true, 2, 2, InputRuleDecision.Run)]
        public void Evaluate(bool requireAll, bool fail, int total, int withoutData, InputRuleDecision expected)
        {
            NodeInputRule.Evaluate(new NodeInputRule(requireAll, fail), total, withoutData).Should().Be(expected);
        }
    }

    public class InputItemsByHandleBuilderTests
    {
        private static WorkflowItemExecutionEntity Item(string id, string nodeId, string nodeExecutionId, int index, string branch = "source") => new()
        {
            Id = id, WorkflowExecutionId = "e", TenantId = "t", NodeId = nodeId, NodeExecutionId = nodeExecutionId, NodeName = nodeId,
            Branch = branch, ItemIndex = index, ParentItemIds = new(), AncestorMap = new(), Data = new NodeOutputItemData(),
        };

        private static EdgeEnity Edge(string source, string targetHandle, string sourceHandle = "source") => new()
        {
            Id = $"{source}-{targetHandle}", Source = source, Target = "m", SourceHandle = sourceHandle, TargetHandle = targetHandle,
        };

        private static NodeExecutionEntity Row(string id, string nodeId, int runIndex) => new()
        {
            Id = id, NodeId = nodeId, NodeName = nodeId, NodeType = "x", NodeVersion = "v1", RunIndex = runIndex, Status = NodeExecutionStatus.Completed,
        };

        [Fact]
        public void GroupsByTargetHandle_InEdgeOrder_ThenRunIndex_ThenItemIndex()
        {
            var items = new[]
            {
                Item("p1-run2-0", "p1", "r2", 0), Item("p1-run1-1", "p1", "r1", 1), Item("p1-run1-0", "p1", "r1", 0),
                Item("p2-0", "p2", "r3", 0), Item("p3-0", "p3", "r4", 0),
            };
            var edges = new[] { Edge("p1", "merge-handle-target-1"), Edge("p3", "merge-handle-target-1"), Edge("p2", "merge-handle-target-2") };
            var rows = new[] { Row("r1", "p1", 1), Row("r2", "p1", 5), Row("r3", "p2", 2), Row("r4", "p3", 3) };

            var byHandle = InputItemsByHandleBuilder.Build(edges, items, rows);

            byHandle["merge-handle-target-1"].Select(i => i.Id).Should().Equal("p1-run1-0", "p1-run1-1", "p1-run2-0", "p3-0");
            byHandle["merge-handle-target-2"].Select(i => i.Id).Should().Equal("p2-0");
        }

        [Fact]
        public void SameSourceOnTwoHandles_GoesToBoth_AndBlankHandleIsEmptyKey()
        {
            var items = new[] { Item("a", "p1", "r1", 0) };
            var edges = new[] { Edge("p1", "merge-handle-target-1"), Edge("p1", "merge-handle-target-2"), Edge("p1", " ") };

            var byHandle = InputItemsByHandleBuilder.Build(edges, items, [Row("r1", "p1", 1)]);

            byHandle["merge-handle-target-1"].Should().ContainSingle();
            byHandle["merge-handle-target-2"].Should().ContainSingle();
            byHandle[""].Should().ContainSingle();
        }

        [Fact]
        public void MatchesTheEdgesBranch()
        {
            var items = new[] { Item("t", "if", "r1", 0, "if-true"), Item("f", "if", "r1", 1, "if-false") };
            var edges = new[] { Edge("if", "merge-handle-target-1", "if-false") };

            InputItemsByHandleBuilder.Build(edges, items, [Row("r1", "if", 1)])["merge-handle-target-1"].Select(i => i.Id).Should().Equal("f");
        }
    }

    /// <summary>SPEC3 through the real engine: the input rule and Merge input order.</summary>
    public class MergeEngineTests
    {
        private sealed class CountingExecutor(string type) : INodeExecutor
        {
            public int Calls;
            public string NodeType => type;
            public string Version => "v1";
            public Task<NodeExecutionResult> RunAsync(NodeExecutionContext context)
            {
                Calls++;
                return Task.FromResult(NodeExecutionResult.Successful([]));
            }
        }

        private sealed record Harness(WorkflowEngineService Engine, WorkflowExecutionEntity Execution, List<WorkflowItemExecutionEntity> Saved, Func<string?> StoredError);

        private static NodeExecutionEntity Parent(string id, int runIndex, Dictionary<string, int> counts) => new()
        {
            Id = $"ne-{id}", NodeId = id, NodeName = id, NodeType = "x", NodeVersion = "v1", RunIndex = runIndex,
            Status = NodeExecutionStatus.Completed, OutputCountsByBranch = counts, OutputItemCount = counts.Values.Sum(),
        };

        private static WorkflowItemExecutionEntity ParentItem(string id, string nodeId, int index) => new()
        {
            Id = id, WorkflowExecutionId = "exec-1", TenantId = "t1", NodeId = nodeId, NodeExecutionId = $"ne-{nodeId}", NodeName = nodeId,
            Branch = "source", ItemIndex = index, ParentItemIds = new(), AncestorMap = new(), Data = new NodeOutputItemData { Output = new BsonDocument("id", id) },
        };

        private static Harness Build(string type, string version, BsonDocument settings, bool secondParentHasData, INodeExecutor executor)
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1", TenantId = "t1", WorkflowId = "wf", WorkflowName = "wf",
                ExecutionMode = WorkflowExecutionMode.Production,
                TriggerMetadata = new TriggerMetadata(),
                WorkflowSnapshot = new WorkflowEntity
                {
                    TenantId = "t1",
                    Nodes =
                    [
                        new NodeEntity { Id = "p1", Name = "p1", Category = "transform", Type = "x", Version = "v1", Position = new Position() },
                        new NodeEntity { Id = "p2", Name = "p2", Category = "transform", Type = "x", Version = "v1", Position = new Position() },
                        new NodeEntity
                        {
                            Id = "m", Name = "Merge", Category = "transform", Type = type, Version = version, Position = new Position(),
                            Parameters = new BsonDocument { { "mode", "append" }, { "numberInputs", 2 } }, Settings = settings,
                        },
                    ],
                    // Edge order is input 2 first, so the output order shows the handle order wins.
                    Edges =
                    [
                        new EdgeEnity { Id = "e2", Source = "p2", Target = "m", SourceHandle = "source", TargetHandle = "merge-handle-target-2" },
                        new EdgeEnity { Id = "e1", Source = "p1", Target = "m", SourceHandle = "source", TargetHandle = "merge-handle-target-1" },
                    ],
                    TestMeta = new TestWorkflowMeta(),
                },
                NodeExecutions =
                [
                    Parent("p1", 1, new() { ["source"] = 2 }),
                    Parent("p2", 2, secondParentHasData ? new() { ["source"] = 1 } : new()),
                ],
            };
            var parentItems = new List<WorkflowItemExecutionEntity> { ParentItem("b1", "p2", 0), ParentItem("a1", "p1", 0), ParentItem("a2", "p1", 1) };
            if (!secondParentHasData) parentItems.RemoveAt(0);

            var saved = new List<WorkflowItemExecutionEntity>();
            string? error = null;
            var repository = new Mock<IWorkflowExecutionRepository> { DefaultValue = DefaultValue.Empty };
            repository.Setup(r => r.GetByIdAsync("exec-1", "t1")).ReturnsAsync(execution);
            repository.Setup(r => r.GetItemsByNodeIdsAsync(It.IsAny<string>(), It.IsAny<List<Dictionary<string, string>>>(), It.IsAny<string>()))
                .ReturnsAsync(parentItems);
            repository.Setup(r => r.TryAddFirstNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            repository.Setup(r => r.TryAddNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            repository.Setup(r => r.AddItemsAsync(It.IsAny<string>(), It.IsAny<List<WorkflowItemExecutionEntity>>()))
                .Callback((string _, List<WorkflowItemExecutionEntity> added) => saved.AddRange(added))
                .Returns(Task.CompletedTask);
            repository.Setup(r => r.AtomicUpdateNodeExecutionFailedAsync("exec-1", "t1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Dictionary<string, int>>()))
                .Callback((string _, string _, string _, string e, int _, Dictionary<string, int> _) => error = e)
                .Returns(Task.CompletedTask);

            var engine = new WorkflowEngineService(repository.Object, [executor], Mock.Of<IMessageClient>(),
                NullLogger<WorkflowEngineService>.Instance, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IServiceProvider>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance));
            return new Harness(engine, execution, saved, () => error);
        }

        private static AddExcuationNodeEvent Event() => new() { TenantId = "t1", WorkflowId = "wf", WorkflowExecutionId = "exec-1", NodeId = "m" };

        private static readonly BsonDocument AllSkip = new() { { "runWhen", "allInputsHaveData" }, { "ifInputMissing", "skip" } };
        private static readonly BsonDocument AllFail = new() { { "runWhen", "allInputsHaveData" }, { "ifInputMissing", "fail" } };

        [Fact]
        public async Task H1_RealMerge_AppendsByHandleOrder()
        {
            var h = Build("merge", "v2", new BsonDocument(), secondParentHasData: true, new TransformMergeV2Node());

            await h.Engine.RunNodeInProcessAsync(Event());

            h.StoredError().Should().BeNull();
            h.Saved.Select(i => i.Data.Output["id"].AsString).Should().Equal("a1", "a2", "b1");
        }

        [Fact]
        public async Task C10_NodeWithoutTheSetting_RunsEvenWhenAnInputSentNothing()
        {
            var executor = new CountingExecutor("x");
            var h = Build("x", "v1", new BsonDocument(), secondParentHasData: false, executor);

            await h.Engine.RunNodeInProcessAsync(Event());

            executor.Calls.Should().Be(1);
            h.StoredError().Should().BeNull();
        }

        [Fact]
        public async Task H5_AllInputsHaveData_Runs()
        {
            var executor = new CountingExecutor("x");
            var h = Build("x", "v1", AllFail, secondParentHasData: true, executor);

            await h.Engine.RunNodeInProcessAsync(Event());

            executor.Calls.Should().Be(1);
            h.StoredError().Should().BeNull();
        }

        [Fact]
        public async Task H6_MissingInput_Skip_CompletesWithNoItems_WithoutRunningTheNode()
        {
            var executor = new CountingExecutor("x");
            var h = Build("x", "v1", AllSkip, secondParentHasData: false, executor);

            await h.Engine.RunNodeInProcessAsync(Event());

            executor.Calls.Should().Be(0);
            h.StoredError().Should().BeNull();
            h.Saved.Should().BeEmpty();
            h.Execution.NodeExecutions.Last(ne => ne.NodeId == "m").Status.Should().Be(NodeExecutionStatus.Completed);
        }

        [Fact]
        public async Task C1_C16_MissingInput_Fail_FailsWithTheVisibleMessage()
        {
            var executor = new CountingExecutor("x");
            var h = Build("x", "v1", AllFail, secondParentHasData: false, executor);

            await h.Engine.RunNodeInProcessAsync(Event());

            executor.Calls.Should().Be(0);
            h.StoredError().Should().Be("Required input data is missing: 1 of 2 inputs sent no items.");
            h.Execution.Status.Should().Be(WorkflowExecutionStatus.Failed);
            h.Execution.ErrorMessage.Should().Be("Required input data is missing: 1 of 2 inputs sent no items.");
        }
    }
}
