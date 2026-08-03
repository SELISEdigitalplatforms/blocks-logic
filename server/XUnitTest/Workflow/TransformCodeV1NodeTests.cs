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
        private static WorkflowItemExecutionEntity Item(string id, BsonDocument output)
            => new()
            {
                Id = id,
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                NodeId = "node-1",
                NodeExecutionId = "ne-1",
                NodeName = "Node1",
                Branch = "source",
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
                Item("a", new BsonDocument { { "name", "abc" }, { "n", 1 } }),
                Item("b", new BsonDocument { { "name", "xyz" }, { "n", 2 } }),
            };
            var ctx = Context(items, mode: "runOnceForAllItems",
                jsCode: "return $items.map(i => ({ json: { tag: i.name.toUpperCase() } }));");

            var node = new TransformCodeV1Node();
            var result = await node.RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Data.Output["json"].AsBsonDocument["tag"].AsString.Should().Be("ABC");
            result.OutputItems[1].Data.Output["json"].AsBsonDocument["tag"].AsString.Should().Be("XYZ");
            result.OutputItems[0].Branch.Should().Be("source");
            result.OutputItems[0].ParentItemIds.Should().BeEquivalentTo(new[] { "a", "b" });
        }

        [Fact]
        public async Task RunAsync_AllItems_NoInputs_StillSucceeds()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), mode: "runOnceForAllItems", jsCode: "return [{ json: { ok: true } }];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(1);
            result.OutputItems[0].Data.Output["json"].AsBsonDocument["ok"].AsBoolean.Should().BeTrue();
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
                jsCode: "return { json: { doubled: $json.n * 2 } };");

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
                const out = {
                    json: {
                        answer: $node['Prev'][0].answer,
                        fromJson: $json.value,
                        idx: $itemIndex,
                        viaInput: $input.value
                    }
                };
                return [out];
                """);
            ctx.AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>
            {
                { "Prev", new List<WorkflowItemExecutionEntity> { ancestor } },
            };

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var json = result.OutputItems[0].Data.Output["json"].AsBsonDocument;
            json["answer"].ToInt32().Should().Be(42);
            json["fromJson"].AsString.Should().Be("alpha");
            json["idx"].ToInt32().Should().Be(0);
            json["viaInput"].AsString.Should().Be("alpha");
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
            // Attempt to reach the host CLR — must not crash and must return Failed.
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), jsCode: """
                try {
                    const t = System.IO.File;
                    return [{ json: { leaked: typeof t } }];
                } catch (e) {
                    return [{ json: { error: e.message } }];
                }
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            // The script's catch arm produces a JSON item with an error message; either way no CLR escape.
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var json = result.OutputItems[0].Data.Output["json"].AsBsonDocument;
            json.Contains("error").Should().BeTrue(); // System.IO.File should not be reachable from script
        }

        [Fact]
        public async Task RunAsync_NoNetworkImport_IsBlocked()
        {
            // The user has no way to import or require anything; this just verifies
            // the engine has no built-in fetch and that attempts are inert.
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), jsCode: """
                const fetched = typeof fetch;
                const imported = (() => { try { return require('fs'); } catch (e) { return 'blocked:' + e.message; } })();
                return [{ json: { fetched, imported } }];
                """);

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var json = result.OutputItems[0].Data.Output["json"].AsBsonDocument;
            json["fetched"].AsString.Should().Be("undefined");
            json["imported"].AsString.Should().StartWith("blocked:");
        }

        [Fact]
        public async Task RunAsync_InfiniteLoop_TimesOutAsFailure()
        {
            var ctx = Context(new List<WorkflowItemExecutionEntity>(), jsCode: "while (true) {}");

            // Cap test runtime so a hung engine doesn't deadlock CI.
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
                jsCode: "return new Array(10001).fill({ json: {} });");

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
        public async Task RunAsync_AllItems_ReturnItemsUnchanged_PreservesDataAndCount()
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
            result.OutputItems[0].Data.Output["name"].AsString.Should().Be("abc");
            result.OutputItems[0].Data.Output["n"].ToInt32().Should().Be(1);
            result.OutputItems[1].Data.Output["name"].AsString.Should().Be("xyz");
            result.OutputItems[1].Data.Output["n"].ToInt32().Should().Be(2);
            result.OutputItems[0].ParentItemIds.Should().BeEquivalentTo(new[] { "a", "b" });
            result.OutputItems[1].ParentItemIds.Should().BeEquivalentTo(new[] { "a", "b" });
        }

        [Fact]
        public async Task RunAsync_AllItems_ReturnNonArray_ReturnsFailed()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "name", "abc" } }),
            };
            var ctx = Context(items, mode: "runOnceForAllItems", jsCode: "return { name: 'abc' };");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("must return an array");
        }

        [Fact]
        public async Task RunAsync_PerItem_ReturnNonObject_ReturnsFailed()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "n", 1 } }),
            };
            var ctx = Context(items, mode: "runOnceForEachItem", jsCode: "return [1, 2, 3];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("must return a single object");
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