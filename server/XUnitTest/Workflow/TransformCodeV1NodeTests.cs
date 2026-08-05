using DomainService.Workflow.Entities;
using DomainService.Workflow.Nodes;
using DomainService.Workflow.Nodes.TransformCodeV1;
using FluentAssertions;
using MongoDB.Bson;

namespace XUnitTest.Workflow
{
    public class TransformCodeV1NodeTests
    {
        // ----- Test data helpers -------------------------------------------------

        private const string AllMode = "all";
        private const string EachMode = "each";

        private static WorkflowItemExecutionEntity Item(
            string id,
            BsonDocument output,
            string branch = "source",
            int itemIndex = 0,
            string nodeName = "Node1",
            List<string>? parentItemIds = null)
            => new()
            {
                Id = id,
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                NodeId = "node-1",
                NodeExecutionId = "ne-1",
                NodeName = nodeName,
                Branch = branch,
                ItemIndex = itemIndex,
                ParentItemIds = parentItemIds ?? new List<string>(),
                AncestorMap = new Dictionary<string, string>(),
                Data = new NodeOutputItemData { Output = output },
            };

        private static NodeExecutionContext Context(
            List<WorkflowItemExecutionEntity> items,
            string mode = AllMode,
            string? jsCode = null)
        {
            var parameters = new BsonDocument
            {
                { "Mode", mode },
                { "Language", "js" },
                { "Script", jsCode ?? string.Empty },
            };
            return new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
                AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
            };
        }

        private static NodeExecutionContext ContextWithAncestor(
            List<WorkflowItemExecutionEntity> items,
            string ancestorName,
            WorkflowItemExecutionEntity ancestor,
            string mode = AllMode,
            string? jsCode = null)
        {
            var ctx = Context(items, mode, jsCode);
            ctx.AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>
            {
                { ancestorName, new List<WorkflowItemExecutionEntity> { ancestor } },
            };
            return ctx;
        }

        // ----- Metadata ----------------------------------------------------------

        [Fact]
        public void NodeMetadata_IsExpected()
        {
            var node = new TransformCodeV1Node();
            node.NodeType.Should().Be("code");
            node.Version.Should().Be("v1");
        }

        // ----- All mode: $items + $node.<X>.all()/first() -----------------------

        [Fact]
        public async Task RunAsync_AllMode_MapsOverItems()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "name", "abc" } }),
                Item("b", new BsonDocument { { "name", "xyz" } }),
            };
            var ctx = Context(items, AllMode,
                "return $items.map(i => ({ tag: i.json.name.toUpperCase() }));");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Data.Output["tag"].AsString.Should().Be("ABC");
            result.OutputItems[1].Data.Output["tag"].AsString.Should().Be("XYZ");
            // No __id forwarded → fallback to all inputs.
            result.OutputItems[0].ParentItemIds.Should().BeEquivalentTo(new[] { "a", "b" });
            result.OutputItems[1].ParentItemIds.Should().BeEquivalentTo(new[] { "a", "b" });
        }

        [Fact]
        public async Task RunAsync_AllMode_NoInputs_Succeeds()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode,
                "return [{ ok: true }];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(1);
            result.OutputItems[0].Data.Output["ok"].AsBoolean.Should().BeTrue();
        }

        [Fact]
        public async Task RunAsync_AllMode_ForwardsSourceId_LinksEachOutputToItsOwnSource()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }, branch: "branchA"),
                Item("b", new BsonDocument { { "n", 2 } }, branch: "branchB"),
                Item("c", new BsonDocument { { "n", 3 } }, branch: "branchC"),
            };
            var ctx = Context(items, AllMode, """
                return $items.map(it => ({ doubled: it.json.n * 2, __id: it.json.__id }));
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(3);
            result.OutputItems[0].Data.Output["doubled"].ToDouble().Should().Be(2);
            result.OutputItems[1].Data.Output["doubled"].ToDouble().Should().Be(4);
            result.OutputItems[2].Data.Output["doubled"].ToDouble().Should().Be(6);

            // __id is stripped from persisted output but drives lineage.
            result.OutputItems[0].Data.Output.AsBsonDocument.Contains("__id").Should().BeFalse();
            result.OutputItems[0].ParentItemIds.Should().BeEquivalentTo(new[] { "a" });
            result.OutputItems[0].Branch.Should().Be("branchA");
            result.OutputItems[0].Data.Input["n"].ToInt32().Should().Be(1);

            result.OutputItems[1].ParentItemIds.Should().BeEquivalentTo(new[] { "b" });
            result.OutputItems[1].Branch.Should().Be("branchB");
            result.OutputItems[1].Data.Input["n"].ToInt32().Should().Be(2);

            result.OutputItems[2].ParentItemIds.Should().BeEquivalentTo(new[] { "c" });
            result.OutputItems[2].Branch.Should().Be("branchC");
            result.OutputItems[2].Data.Input["n"].ToInt32().Should().Be(3);
        }

        [Fact]
        public async Task RunAsync_AllMode_ForwardsArbitrarySourceId_LinksAllOutputsToSameSource()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }, branch: "branchA"),
                Item("b", new BsonDocument { { "n", 2 } }, branch: "branchB"),
                Item("c", new BsonDocument { { "n", 3 } }, branch: "branchC"),
            };
            // Every output reparents itself to input[2] ("c") by forwarding that __id.
            var ctx = Context(items, AllMode, """
                return $items.map(it => ({ n: it.json.n, __id: $items[2].json.__id }));
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(3);
            result.OutputItems[0].Data.Output["n"].ToInt32().Should().Be(1);
            result.OutputItems[1].Data.Output["n"].ToInt32().Should().Be(2);
            result.OutputItems[2].Data.Output["n"].ToInt32().Should().Be(3);
            foreach (var output in result.OutputItems)
            {
                output.ParentItemIds.Should().BeEquivalentTo(new[] { "c" });
                output.Branch.Should().Be("branchC");
                output.Data.Input["n"].ToInt32().Should().Be(3);
            }
        }

        [Fact]
        public async Task RunAsync_AllMode_NoSourceId_FallsBackToAllInputs()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }),
                Item("b", new BsonDocument { { "n", 2 } }),
            };
            var ctx = Context(items, AllMode, "return $items.map(it => ({ n: it.json.n }));");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(2);
            foreach (var output in result.OutputItems)
            {
                output.ParentItemIds.Should().BeEquivalentTo(new[] { "a", "b" });
                output.Branch.Should().Be("source");
                output.Data.Input.AsBsonDocument.ElementCount.Should().Be(0);
            }
        }

        [Fact]
        public async Task RunAsync_AllMode_ReturnsItemsUnchanged_PreservesDataAndLineage()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "name", "abc" }, { "n", 1 } }),
                Item("b", new BsonDocument { { "name", "xyz" }, { "n", 2 } }),
            };
            var ctx = Context(items, AllMode, "return $items;");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Data.Output.AsBsonDocument.Contains("__id").Should().BeFalse();
            result.OutputItems[0].Data.Output["name"].AsString.Should().Be("abc");
            result.OutputItems[0].Data.Output["n"].ToInt32().Should().Be(1);
            result.OutputItems[1].Data.Output["name"].AsString.Should().Be("xyz");
            result.OutputItems[1].Data.Output["n"].ToInt32().Should().Be(2);
            // Each output inherits __id from its source input → correct lineage.
            result.OutputItems[0].ParentItemIds.Should().BeEquivalentTo(new[] { "a" });
            result.OutputItems[1].ParentItemIds.Should().BeEquivalentTo(new[] { "b" });
        }

        [Fact]
        public async Task RunAsync_AllMode_ReturnsSingleObject_ProducesOneItem()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "name", "abc" } }),
            };
            var ctx = Context(items, AllMode, "return { name: 'abc' };");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(1);
            result.OutputItems[0].Data.Output["name"].AsString.Should().Be("abc");
        }

        [Fact]
        public async Task RunAsync_AllMode_NodeAccessor_ResolvesAncestor()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "value", "alpha" } }),
            };
            var ancestor = Item("prev-1", new BsonDocument { { "answer", 42 } });
            var ctx = ContextWithAncestor(items, "Prev", ancestor, AllMode, """
                return [{
                    answer: $node['Prev'].first().json.answer,
                    value:  $items[0].json.value,
                }];
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output["answer"].ToInt32().Should().Be(42);
            result.OutputItems[0].Data.Output["value"].AsString.Should().Be("alpha");
        }

        [Fact]
        public async Task RunAsync_AllMode_NodeAccessor_AllReturnsEveryItem()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "value", "alpha" } }),
            };
            var ancestors = new List<WorkflowItemExecutionEntity>
            {
                Item("p1", new BsonDocument { { "v", 1 } }),
                Item("p2", new BsonDocument { { "v", 2 } }),
                Item("p3", new BsonDocument { { "v", 3 } }),
            };
            var ctx = Context(items, AllMode, """
                return [{ values: $node['Prev'].all().map(n => n.json.v) }];
                """);
            ctx.AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>
            {
                { "Prev", ancestors },
            };

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output["values"].AsBsonArray
                .Select(t => t.ToInt32()).Should().Equal(1, 2, 3);
        }

        // ----- Each mode: $json / $item / $node.<X>.json -----------------------

        [Fact]
        public async Task RunAsync_PerItem_ResolvesJsonPerInput()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }),
                Item("b", new BsonDocument { { "n", 2 } }),
                Item("c", new BsonDocument { { "n", 3 } }),
            };
            var ctx = Context(items, EachMode, "return { doubled: $json.n * 2 };");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(3);
            result.OutputItems[0].Data.Output["doubled"].ToDouble().Should().Be(2);
            result.OutputItems[1].Data.Output["doubled"].ToDouble().Should().Be(4);
            result.OutputItems[2].Data.Output["doubled"].ToDouble().Should().Be(6);
            // Each output is linked to its own input.
            result.OutputItems[0].ParentItemIds.Should().BeEquivalentTo(new[] { "a" });
            result.OutputItems[1].ParentItemIds.Should().BeEquivalentTo(new[] { "b" });
            result.OutputItems[2].ParentItemIds.Should().BeEquivalentTo(new[] { "c" });
        }

        [Fact]
        public async Task RunAsync_PerItem_DirectFieldAccess()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "name", "abc" } }),
            };
            var ctx = Context(items, EachMode, "return { seen: $item.name };");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output["seen"].AsString.Should().Be("abc");
        }

        [Fact]
        public async Task RunAsync_PerItem_ReturnsArray_FansOutPerInput()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }),
            };
            var ctx = Context(items, EachMode, "return [1, 2, 3];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(3);
            result.OutputItems[0].Data.Output["json"].ToInt32().Should().Be(1);
            result.OutputItems[1].Data.Output["json"].ToInt32().Should().Be(2);
            result.OutputItems[2].Data.Output["json"].ToInt32().Should().Be(3);
            foreach (var output in result.OutputItems)
            {
                output.ParentItemIds.Should().BeEquivalentTo(new[] { "a" });
            }
        }

        [Fact]
        public async Task RunAsync_PerItem_NodeReference_ResolvesAncestor()
        {
            // each-mode walks ParentItemIds and keys $node by the ancestor's NodeName.
            // The walk starts from the current input, so the input must also be present
            // in AncestorNodeOutputs (as the immediate predecessor's output) for its
            // parent chain to be followed.
            var input = Item("a", new BsonDocument { { "value", "alpha" } },
                nodeName: "Current", parentItemIds: new List<string> { "prev-1" });
            var ancestor = Item("prev-1", new BsonDocument { { "answer", 42 } }, nodeName: "Prev");

            var ctx = Context(new List<WorkflowItemExecutionEntity> { input }, EachMode,
                "return { answer: $node['Prev'].json.answer };");
            ctx.AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>
            {
                { "Current", new List<WorkflowItemExecutionEntity> { input } },
                { "Prev", new List<WorkflowItemExecutionEntity> { ancestor } },
            };

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output["answer"].ToInt32().Should().Be(42);
        }

        // ----- Sandbox / security -----------------------------------------------

        [Fact]
        public async Task RunAsync_ClrEscape_IsBlocked()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode, """
                try {
                    const t = System.IO.File;
                    return [{ leaked: typeof t }];
                } catch (e) {
                    return [{ error: e.message }];
                }
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output.AsBsonDocument.Contains("error").Should().BeTrue();
        }

        [Fact]
        public async Task RunAsync_DangerousGlobalsAreUndefined()
        {
            // Network/filesystem/process identifiers must be unreachable (n8n-style sandbox).
            var denylist = new[]
            {
                "require", "process", "fetch", "XMLHttpRequest",
                "http", "https", "fs", "child_process", "net", "dns",
            };
            var types = string.Join(", ", denylist.Select(g => $"typeof {g}"));
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode,
                $"return [{{ types: [{types}] }}];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output["types"].AsBsonArray
                .Select(t => t.AsString).Should().AllBe("undefined");
        }

        [Fact]
        public async Task RunAsync_NoNetworkImport_IsBlocked()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode, """
                const fetched = typeof fetch;
                const imported = (() => { try { return require('fs'); } catch (e) { return 'blocked:' + e.message; } })();
                return [{ fetched, imported }];
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output["fetched"].AsString.Should().Be("undefined");
            result.OutputItems[0].Data.Output["imported"].AsString.Should().StartWith("blocked:");
        }

        // ----- Validation & limits ----------------------------------------------

        [Fact]
        public async Task RunAsync_UnsupportedLanguage_ReturnsFailed()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "x", 1 } }),
            }, AllMode, "x");
            ctx.Parameters = new BsonDocument
            {
                { "Mode", AllMode },
                { "Language", "python" },
                { "Script", "x" },
            };

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("not supported");
        }

        [Fact]
        public async Task RunAsync_MalformedScript_ReturnsFailed()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode, "function () {");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task RunAsync_ScriptThrows_ReturnsFailed()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode,
                "throw new Error('boom');");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("boom");
        }

        [Fact]
        public async Task RunAsync_InfiniteLoop_TimesOutAsFailure()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode, "while (true) {}");

            var run = Task.Run(() => new TransformCodeV1Node().RunAsync(ctx));
            var completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(45)));
            completed.Should().BeSameAs(run, "Jint should enforce its statement/timeout limits");

            var result = await run;
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task RunAsync_OversizedScript_ReturnsFailed()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode,
                new string(' ', (256 * 1024) + 1));

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Script too large");
        }

        [Fact]
        public async Task RunAsync_TooManyOutputItems_ReturnsFailed()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode,
                "return new Array(10001).fill({});");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Too many output items");
        }

        [Fact]
        public async Task RunAsync_OversizedSerializedOutput_ReturnsFailed()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), AllMode,
                "const value = 'x'.repeat(2048); return new Array(9000).fill({ value });");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Output too large");
        }
    }
}
