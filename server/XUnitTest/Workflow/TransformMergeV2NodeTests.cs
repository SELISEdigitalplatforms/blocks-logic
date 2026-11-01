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
        [InlineData(1.0, 2)]
        [InlineData(0.0, 2)]
        [InlineData(-3.0, 2)]
        [InlineData(5.0, 5)]
        [InlineData(10.0, 10)]
        [InlineData(11.0, 10)]
        [InlineData(1e12, 10)]
        public void C3_InputCount_IsTwoToTen(double? value, int expected)
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
            var result = await Run(Params(2), new()
            {
                ["merge-handle-target-1"] = [Item("x1", "p1")],
                ["merge-handle-target-3"] = [Item("y1", "p2")],
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
        public async Task C7_UnsupportedMode_FailsTheStep(string mode)
        {
            var result = await Run(Params(2, mode), new() { ["merge-handle-target-1"] = [Item("a1", "p1")] });
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("mode");
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task Mode_ReadsTheSavedName()
        {
            Ids(await Run(Params(2, "append"), new() { ["merge-handle-target-1"] = [Item("a1", "p1")] })).Should().Equal("a1");
        }

        [Fact]
        public async Task OutputItems_KeepTheParametersAsSaved()
        {
            var saved = Params(2);
            var result = await Run(saved, new() { ["merge-handle-target-1"] = [Item("a1", "p1")] });

            result.OutputItems.Single().Data.Parameters.Should().BeSameAs(saved);
            saved["mode"].AsString.Should().Be("append");
        }

        [Fact]
        public async Task NumberInputsAboveTen_UsesTen()
        {
            var result = await Run(Params(1e12), new()
            {
                ["merge-handle-target-1"] = [Item("a1", "p1")],
                ["merge-handle-target-10"] = [Item("j1", "p10")],
                ["merge-handle-target-11"] = [Item("k1", "p11")],
                ["merge-handle-target-999999999999"] = [Item("z1", "p9")],
            });

            Ids(result).Should().Equal("a1", "j1");
        }

        [Fact]
        public async Task NoInputs_SucceedsWithNoItems()
        {
            (await Run(Params(2), new())).OutputItems.Should().BeEmpty();
        }

        private static BsonDocument RunWhen(string runWhen)
        {
            var parameters = Params(2);
            parameters["runWhen"] = runWhen;
            return parameters;
        }

        private static readonly Dictionary<string, IReadOnlyList<WorkflowItemExecutionEntity>> Input2Empty = new()
        {
            ["merge-handle-target-1"] = [Item("a1", "p1"), Item("a2", "p1", 1)],
            ["merge-handle-target-2"] = [],
        };

        [Fact]
        public async Task RunWhenAll_EveryConnectedInputHasItems_Appends()
        {
            var result = await Run(RunWhen("all"), new()
            {
                ["merge-handle-target-2"] = [Item("b1", "p2")],
                ["merge-handle-target-1"] = [Item("a1", "p1")],
            });

            Ids(result).Should().Equal("a1", "b1");
        }

        [Fact]
        public async Task RunWhenAll_AConnectedInputIsEmpty_SucceedsWithNoItems()
        {
            var result = await Run(RunWhen("all"), Input2Empty);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task RunWhen_MissingMeansAll()
        {
            var result = await Run(Params(2), Input2Empty);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task RunWhenAll_CountsOnlyConnectedInputs()
        {
            // Number of Inputs 3 with only inputs 1 and 2 connected: input 3 is not counted.
            var parameters = Params(3);
            parameters["runWhen"] = "all";
            var result = await Run(parameters, new()
            {
                ["merge-handle-target-1"] = [Item("a1", "p1")],
                ["merge-handle-target-2"] = [Item("b1", "p2")],
            });

            Ids(result).Should().Equal("a1", "b1");
        }

        [Fact]
        public async Task RunWhenAll_TwoEdgesIntoOneInput_CountTogether()
        {
            // A blank handle is input 1: one empty edge and one with items still give input 1 items.
            var result = await Run(RunWhen("all"), new()
            {
                ["merge-handle-target-1"] = [],
                [""] = [Item("a1", "p0")],
                ["merge-handle-target-2"] = [Item("b1", "p2")],
            });

            Ids(result).Should().Equal("a1", "b1");
        }

        [Fact]
        public async Task RunWhenAll_IgnoresInputsAboveN()
        {
            var result = await Run(RunWhen("all"), new()
            {
                ["merge-handle-target-1"] = [Item("a1", "p1")],
                ["merge-handle-target-2"] = [Item("b1", "p2")],
                ["merge-handle-target-3"] = [],
            });

            Ids(result).Should().Equal("a1", "b1");
        }

        [Fact]
        public async Task RunWhenAny_OutputsWhatArrived()
        {
            Ids(await Run(RunWhen("any"), Input2Empty)).Should().Equal("a1", "a2");
        }

        [Fact]
        public async Task RunWhen_UnknownValue_FailsTheStep()
        {
            var result = await Run(RunWhen("sometimes"), Input2Empty);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("runWhen");
            result.OutputItems.Should().BeEmpty();
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

    /// <summary>SPEC3 through the real engine: Merge input order.</summary>
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

        private static Harness Build(string type, string version, BsonDocument settings, bool secondParentHasData, INodeExecutor executor, string? runWhen = null)
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
                            Parameters = runWhen is null
                                ? new BsonDocument { { "mode", "append" }, { "numberInputs", 2 } }
                                : new BsonDocument { { "mode", "append" }, { "numberInputs", 2 }, { "runWhen", runWhen } },
                            Settings = settings,
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
                Mock.Of<IServiceProvider>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                Mock.Of<IWorkflowRepository>());
            return new Harness(engine, execution, saved, () => error);
        }

        private static AddExcuationNodeEvent Event() => new() { TenantId = "t1", WorkflowId = "wf", WorkflowExecutionId = "exec-1", NodeId = "m" };

        [Fact]
        public async Task H1_RealMerge_AppendsByHandleOrder()
        {
            var h = Build("merge", "v2", new BsonDocument(), secondParentHasData: true, new TransformMergeV2Node());

            await h.Engine.RunNodeInProcessAsync(Event());

            h.StoredError().Should().BeNull();
            h.Saved.Select(i => i.Data.Output["id"].AsString).Should().Equal("a1", "a2", "b1");
        }

        [Fact]
        public async Task RunWhenAll_OneInputSentNothing_MergeCompletesWithNoItems()
        {
            var h = Build("merge", "v2", new BsonDocument(), secondParentHasData: false, new TransformMergeV2Node(), runWhen: "all");

            await h.Engine.RunNodeInProcessAsync(Event());

            h.StoredError().Should().BeNull();
            h.Saved.Should().BeEmpty();
            var merge = h.Execution.NodeExecutions.Last(ne => ne.NodeId == "m");
            merge.Status.Should().Be(NodeExecutionStatus.Completed);
            merge.OutputItemCount.Should().Be(0);
        }

        [Fact]
        public async Task RunWhenAny_OneInputSentNothing_MergeOutputsTheOtherInput()
        {
            var h = Build("merge", "v2", new BsonDocument(), secondParentHasData: false, new TransformMergeV2Node(), runWhen: "any");

            await h.Engine.RunNodeInProcessAsync(Event());

            h.StoredError().Should().BeNull();
            h.Saved.Select(i => i.Data.Output["id"].AsString).Should().Equal("a1", "a2");
        }

        [Fact]
        public async Task C10_NodeRunsWhenOneInputSentNothing()
        {
            var executor = new CountingExecutor("x");
            var h = Build("x", "v1", new BsonDocument(), secondParentHasData: false, executor);

            await h.Engine.RunNodeInProcessAsync(Event());

            executor.Calls.Should().Be(1);
            h.StoredError().Should().BeNull();
        }
    }

    /// <summary>
    /// A join waits for every parent. When the last parent finishes with 0 items on its edge, nothing used to
    /// wake the join (only edges that carried items send an event), so it stayed active and the run never ended.
    /// </summary>
    public class JoinWakeUpTests
    {
        private sealed class EmptyExecutor : INodeExecutor
        {
            public string NodeType => "x";
            public string Version => "v1";
            public Task<NodeExecutionResult> RunAsync(NodeExecutionContext context) => Task.FromResult(NodeExecutionResult.Successful([]));
        }

        private static (WorkflowEngineService Engine, List<string> Dispatched) Build(List<string> activeNodeIds, params EdgeEnity[] edges)
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1", TenantId = "t1", WorkflowId = "wf", WorkflowName = "wf",
                ExecutionMode = WorkflowExecutionMode.Production,
                TriggerMetadata = new TriggerMetadata(),
                ActiveNodeIds = activeNodeIds,
                WorkflowSnapshot = new WorkflowEntity
                {
                    TenantId = "t1",
                    Nodes =
                    [
                        new NodeEntity { Id = "p1", Name = "p1", Category = "transform", Type = "x", Version = "v1", Position = new Position() },
                        new NodeEntity { Id = "p2", Name = "p2", Category = "transform", Type = "x", Version = "v1", Position = new Position() },
                        new NodeEntity { Id = "m", Name = "Merge", Category = "transform", Type = "merge", Version = "v2", Position = new Position() },
                        new NodeEntity { Id = "n", Name = "n", Category = "transform", Type = "x", Version = "v1", Position = new Position() },
                    ],
                    Edges = [.. edges],
                    TestMeta = new TestWorkflowMeta(),
                },
                NodeExecutions =
                [
                    new NodeExecutionEntity
                    {
                        Id = "ne-p1", NodeId = "p1", NodeName = "p1", NodeType = "x", NodeVersion = "v1", RunIndex = 1,
                        Status = NodeExecutionStatus.Completed, OutputCountsByBranch = new() { ["source"] = 2 }, OutputItemCount = 2,
                    },
                ],
            };

            var dispatched = new List<string>();
            var repository = new Mock<IWorkflowExecutionRepository> { DefaultValue = DefaultValue.Empty };
            repository.Setup(r => r.GetByIdAsync("exec-1", "t1")).ReturnsAsync(execution);
            repository.Setup(r => r.GetItemsByNodeIdsAsync(It.IsAny<string>(), It.IsAny<List<Dictionary<string, string>>>(), It.IsAny<string>()))
                .ReturnsAsync(new List<WorkflowItemExecutionEntity>());
            repository.Setup(r => r.TryAddFirstNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            repository.Setup(r => r.TryAddNodeExecutionAsync("exec-1", "t1", It.IsAny<NodeExecutionEntity>())).ReturnsAsync(true);
            var messages = new Mock<IMessageClient>();
            messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()))
                .Callback((ConsumerMessage<AddExcuationNodeEvent> message) => dispatched.Add(message.Payload.NodeId))
                .Returns(Task.CompletedTask);

            var engine = new WorkflowEngineService(repository.Object, [new EmptyExecutor(), new TransformMergeV2Node()], messages.Object,
                NullLogger<WorkflowEngineService>.Instance, Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IServiceProvider>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                Mock.Of<IWorkflowRepository>());
            return (engine, dispatched);
        }

        private static EdgeEnity Edge(string source, string target, string targetHandle = "target") =>
            new() { Id = $"{source}-{target}", Source = source, Target = target, SourceHandle = "source", TargetHandle = targetHandle };

        private static readonly EdgeEnity[] IntoMerge = [Edge("p1", "m", "merge-handle-target-1"), Edge("p2", "m", "merge-handle-target-2")];

        private static Task RunP2(WorkflowEngineService engine) =>
            engine.RunNodeAsync(new AddExcuationNodeEvent { TenantId = "t1", WorkflowId = "wf", WorkflowExecutionId = "exec-1", NodeId = "p2" });

        [Fact]
        public async Task LastParentSendsNothing_WakesTheWaitingJoin()
        {
            // p1 already sent items, so the join is active and waiting for p2.
            var (engine, dispatched) = Build(["p2", "m"], IntoMerge);

            await RunP2(engine);

            dispatched.Should().Equal("m");
        }

        [Fact]
        public async Task JoinNotWaiting_IsNotWoken()
        {
            // No other parent has sent the join items yet: it is not active, so it is not started.
            var (engine, dispatched) = Build(["p2"], IntoMerge);

            await RunP2(engine);

            dispatched.Should().BeEmpty();
        }

        [Fact]
        public async Task ANodeWithOneParent_StillDoesNotRunOnZeroItems()
        {
            var (engine, dispatched) = Build(["p2", "n"], Edge("p2", "n"));

            await RunP2(engine);

            dispatched.Should().BeEmpty();
        }
    }
}
