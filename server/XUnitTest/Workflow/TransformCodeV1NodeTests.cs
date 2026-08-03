using System.Text;
using DomainService.Workflow.Entities;
using DomainService.Workflow.Nodes;
using DomainService.Workflow.Nodes.TransformCodeV1;
using FluentAssertions;
using MongoDB.Bson;

namespace XUnitTest.Workflow
{
    public class TransformCodeV1NodeTests
    {
        private static WorkflowItemExecutionEntity Item(
            string id,
            BsonDocument output,
            string branch = "source",
            int itemIndex = 0,
            List<string>? parentItemIds = null)
            => new()
            {
                Id = id,
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                NodeId = "node-1",
                NodeExecutionId = "ne-1",
                NodeName = "Node1",
                Branch = branch,
                ItemIndex = itemIndex,
                ParentItemIds = parentItemIds ?? new List<string>(),
                AncestorMap = new Dictionary<string, string>(),
                Data = new NodeOutputItemData { Output = output },
            };

        private static NodeExecutionContext Context(
            List<WorkflowItemExecutionEntity> items,
            string? mode = "runOnceForAllItems",
            string? jsCode = null,
            BsonDocument? workflowContext = null)
        {
            var parameters = new BsonDocument
            {
                { "Mode", mode ?? "runOnceForAllItems" },
                { "Language", "javascript" },
                { "Script", jsCode ?? string.Empty },
            };
            return new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = workflowContext ?? new BsonDocument(),
                AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
            };
        }

        [Fact]
        public void NodeMetadata_IsExpected()
        {
            var node = new TransformCodeV1Node();
            node.NodeType.Should().Be("code");
            node.Version.Should().Be("v1");
        }

        [Fact]
        public async Task RunAsync_AllItems_ReturnArray_ProducesOutputItems()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "name", "abc" }, { "n", 1 } }, itemIndex: 0),
                Item("b", new BsonDocument { { "name", "xyz" }, { "n", 2 } }, itemIndex: 1),
            };
            var ctx = Context(items, mode: "runOnceForAllItems",
                jsCode: "return $items.map(i => ({ tag: i.json.name.toUpperCase() }));");

            var node = new TransformCodeV1Node();
            var result = await node.RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Data.Output["tag"].AsString.Should().Be("ABC");
            result.OutputItems[1].Data.Output["tag"].AsString.Should().Be("XYZ");
            result.OutputItems[0].Branch.Should().Be("source");
            // No __id forwarded → fallback to all inputs.
            result.OutputItems[0].ParentItemIds.Should().BeEquivalentTo(new[] { "a", "b" });
            result.OutputItems[1].ParentItemIds.Should().BeEquivalentTo(new[] { "a", "b" });
        }

        [Fact]
        public async Task RunAsync_AllItems_NoInputs_StillSucceeds()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), mode: "runOnceForAllItems", jsCode: "return [{ ok: true }];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(1);
            result.OutputItems[0].Data.Output["ok"].AsBoolean.Should().BeTrue();
        }

        [Fact]
        public async Task RunAsync_AllItems_ForwardsSourceId_LinksEachOutputToCorrectSource()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }, branch: "branchA", itemIndex: 0),
                Item("b", new BsonDocument { { "n", 2 } }, branch: "branchB", itemIndex: 1),
                Item("c", new BsonDocument { { "n", 3 } }, branch: "branchC", itemIndex: 2),
            };
            var ctx = Context(items, mode: "runOnceForAllItems", jsCode: """
                return $items.map(it => ({ doubled: it.json.n * 2, __id: it.json.__id }));
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(3);
            result.OutputItems[0].Data.Output["doubled"].ToDouble().Should().Be(2);
            result.OutputItems[1].Data.Output["doubled"].ToDouble().Should().Be(4);
            result.OutputItems[2].Data.Output["doubled"].ToDouble().Should().Be(6);

            // __id is stripped from persisted output but used for lineage.
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
        public async Task RunAsync_AllItems_NoSourceId_FallsBackToAllInputs()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }, itemIndex: 0),
                Item("b", new BsonDocument { { "n", 2 } }, itemIndex: 1),
            };
            var ctx = Context(items, mode: "runOnceForAllItems", jsCode: """
                return $items.map(it => ({ n: it.json.n }));
                """);

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
        public async Task RunAsync_PerItem_ForwardsSourceId_LinksToDifferentSource()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }, branch: "sourceA", itemIndex: 0),
                Item("b", new BsonDocument { { "n", 2 } }, branch: "sourceB", itemIndex: 1),
                Item("c", new BsonDocument { { "n", 3 } }, branch: "sourceC", itemIndex: 2),
            };
            // Each iteration reparents its output to input[2] ("c") by forwarding that __id.
            var ctx = Context(items, mode: "runOnceForEachItem", jsCode: """
                return { tookFrom: 'index2', n: $json.n, __id: $items[2].json.__id };
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
                output.Branch.Should().Be("sourceC");
                output.Data.Input["n"].ToInt32().Should().Be(3);
                output.Data.Output["tookFrom"].AsString.Should().Be("index2");
            }
        }

        [Fact]
        public async Task RunAsync_PerItem_DirectFieldAccess()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "name", "abc" } }, itemIndex: 0),
            };
            var ctx = Context(items, mode: "runOnceForEachItem", jsCode: """
                return { seen: $item.name };
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output["seen"].AsString.Should().Be("abc");
        }

        [Fact]
        public async Task RunAsync_PerItem_ResolvesJsonPerInput()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }),
                Item("b", new BsonDocument { { "n", 2 } }),
                Item("c", new BsonDocument { { "n", 3 } }),
            };
            var ctx = Context(items, mode: "runOnceForEachItem",
                jsCode: "return { doubled: $json.n * 2 };");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(3);
            result.OutputItems[0].Data.Output["doubled"].ToDouble().Should().Be(2);
            result.OutputItems[1].Data.Output["doubled"].ToDouble().Should().Be(4);
            result.OutputItems[2].Data.Output["doubled"].ToDouble().Should().Be(6);
            result.OutputItems[0].ParentItemIds.Should().BeEquivalentTo(new[] { "a" });
            result.OutputItems[1].ParentItemIds.Should().BeEquivalentTo(new[] { "b" });
            result.OutputItems[2].ParentItemIds.Should().BeEquivalentTo(new[] { "c" });
        }

        [Fact]
        public async Task RunAsync_ContextAndNodeGlobals_Resolve()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "value", "alpha" } }),
            };
            var ancestor = Item("prev-1", new BsonDocument { { "answer", 42 } });
            var ctx = Context(items, jsCode: """
                return [{
                    answer: $node['Prev'].first().json.answer,
                    value: $items[0].json.value,
                }];
                """);
            ctx.AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>
            {
                { "Prev", new List<WorkflowItemExecutionEntity> { ancestor } },
            };

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output["answer"].ToInt32().Should().Be(42);
            result.OutputItems[0].Data.Output["value"].AsString.Should().Be("alpha");
        }

        [Fact]
        public async Task RunAsync_UnsupportedLanguage_ReturnsFailed()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "x", 1 } }),
            }, jsCode: "x");
            ctx.Parameters = new BsonDocument
            {
                { "Mode", "runOnceForAllItems" },
                { "Language", "python" },
                { "Script", "x" },
            };

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("not supported");
        }

        [Fact]
        public async Task RunAsync_MalformedScript_ReturnsFailedNoException()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), jsCode: "function () {");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task RunAsync_ScriptThrows_ReturnsFailed()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), jsCode: "throw new Error('boom');");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("boom");
        }

        [Fact]
        public async Task RunAsync_ClrEscape_SystemIoFile_IsRejected()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), jsCode: """
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
        public async Task RunAsync_NoNetworkImport_IsBlocked()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), jsCode: """
                const fetched = typeof fetch;
                const imported = (() => { try { return require('fs'); } catch (e) { return 'blocked:' + e.message; } })();
                return [{ fetched, imported }];
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems[0].Data.Output["fetched"].AsString.Should().Be("undefined");
            result.OutputItems[0].Data.Output["imported"].AsString.Should().StartWith("blocked:");
        }

        [Fact]
        public async Task RunAsync_DangerousGlobalsAreUndefined()
        {
            // Every network/filesystem/process identifier must be unreachable
            // from user scripts (n8n-style sandbox).
            var denylist = new[]
            {
                "require", "process", "fetch", "XMLHttpRequest",
                "http", "https", "fs", "child_process", "net", "dns",
            };
            var types = string.Join(", ", denylist.Select(g => $"typeof {g}"));
            var ctx = Context(new List<WorkflowItemExecutionEntity>(),
                jsCode: $"return [{{ types: [{types}] }}];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var actualTypes = result.OutputItems[0].Data.Output["types"].AsBsonArray;
            actualTypes.Select(t => t.AsString).Should().AllBe("undefined");
        }

        [Fact]
        public async Task RunAsync_InfiniteLoop_TimesOutAsFailure()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), jsCode: "while (true) {}");

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
            var ctx = Context(
                new List<WorkflowItemExecutionEntity>(),
                jsCode: new string(' ', (256 * 1024) + 1));

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Script too large");
        }

        [Fact]
        public async Task RunAsync_TooManyOutputItems_ReturnsFailed()
        {
            var ctx = Context(
                new List<WorkflowItemExecutionEntity>(),
                jsCode: "return new Array(10001).fill({});");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Too many output items");
        }

        [Fact]
        public async Task RunAsync_TooManyInputItems_ReturnsFailed()
        {
            var items = Enumerable.Range(0, 1001)
                .Select(index => Item($"item-{index}", new BsonDocument()))
                .ToList();
            var ctx = Context(items, jsCode: "return [];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Too many items: 1001");
        }

        [Fact]
        public async Task RunAsync_AllItems_ReturnItemsUnchanged_PreservesDataAndLineage()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "name", "abc" }, { "n", 1 } }),
                Item("b", new BsonDocument { { "name", "xyz" }, { "n", 2 } }),
            };
            var ctx = Context(items, mode: "runOnceForAllItems", jsCode: "return $items;");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(2);
            // __id is stripped from the persisted output.
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
        public async Task RunAsync_AllItems_ReturnsSingleObject_ProducesOneItem()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "name", "abc" } }),
            };
            var ctx = Context(items, mode: "runOnceForAllItems", jsCode: "return { name: 'abc' };");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(1);
            result.OutputItems[0].Data.Output["name"].AsString.Should().Be("abc");
        }

        [Fact]
        public async Task RunAsync_PerItem_ReturnsArray_ProducesMultipleItemsPerInput()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }),
            };
            var ctx = Context(items, mode: "runOnceForEachItem", jsCode: "return [1, 2, 3];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(3);
            result.OutputItems[0].Data.Output["json"].ToInt32().Should().Be(1);
            result.OutputItems[1].Data.Output["json"].ToInt32().Should().Be(2);
            result.OutputItems[2].Data.Output["json"].ToInt32().Should().Be(3);
            // Each output is linked to the single input item.
            foreach (var output in result.OutputItems)
            {
                output.ParentItemIds.Should().BeEquivalentTo(new[] { "a" });
            }
        }

        [Fact]
        public async Task RunAsync_OversizedSerializedOutput_ReturnsFailed()
        {
            var ctx = Context(
                new List<WorkflowItemExecutionEntity>(),
                jsCode: "const value = 'x'.repeat(2048); return new Array(9000).fill({ value });");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Output too large");
        }
    }
}
