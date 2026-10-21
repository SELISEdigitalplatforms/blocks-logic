using System.Runtime.CompilerServices;
using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.TransformSplitOutV2;
using Workflow.DomainService.Services;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC2 Split Out: one item per list element, n8n semantics, lineage kept.</summary>
    public class TransformSplitOutV2NodeTests
    {
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
            Data = new NodeOutputItemData { Output = output },
        };

        // Tests write fields and output names as comma lists for brevity: "names, scores" with "name, score"
        // becomes [{ fieldName: names, outputFieldName: name }, { fieldName: scores, outputFieldName: score }].
        private static BsonDocument Params(string field, string include = "noOtherFields", string fieldsToInclude = "", string destination = "", bool disableDotNotation = false)
        {
            var names = field.Split(',');
            var outputs = destination.Split(',');
            var fields = new BsonArray(names.Select((name, i) => new BsonDocument
            {
                { "fieldName", name },
                { "outputFieldName", i < outputs.Length ? outputs[i] : "" },
            }));
            return new BsonDocument
            {
                { "fields", fields },
                { "include", include },
                { "fieldsToInclude", new BsonArray(fieldsToInclude.Split(',')) },
                { "disableDotNotation", disableDotNotation },
            };
        }

        private static Task<NodeExecutionResult> Run(BsonDocument parameters, params BsonValue[] outputs)
        {
            var items = outputs.Select((o, i) => Item($"x{i + 1}", o)).ToList();
            return new TransformSplitOutV2Node().RunAsync(new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
            });
        }

        private static BsonDocument Doc(string json) => BsonDocument.Parse(json);

        private static List<string> Outputs(NodeExecutionResult result)
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            return result.OutputItems.Select(o => o.Data.Output.ToJson()).ToList();
        }

        private static readonly BsonDocument Order = Doc("{ \"orderId\": 7, \"lines\": [ { \"sku\": \"A\", \"qty\": 1 }, { \"sku\": \"B\", \"qty\": 2 } ] }");

        [Fact]
        public async Task Example1_SpreadsObjects_AndKeepsLineage()
        {
            var result = await Run(Params("lines"), Order);

            Outputs(result).Should().Equal("{ \"sku\" : \"A\", \"qty\" : 1 }", "{ \"sku\" : \"B\", \"qty\" : 2 }");
            result.OutputItems.Should().OnlyContain(o => o.Branch == "source" && o.ParentItemIds!.SequenceEqual(new[] { "x1" }));
            result.OutputItems.Should().OnlyContain(o => o.Data.Input == Order);
        }

        [Fact]
        public async Task Example2_AllOtherFields_WithADestinationName()
        {
            Outputs(await Run(Params("lines", "allOtherFields", destination: "line"), Order)).Should().Equal(
                "{ \"orderId\" : 7, \"line\" : { \"sku\" : \"A\", \"qty\" : 1 } }",
                "{ \"orderId\" : 7, \"line\" : { \"sku\" : \"B\", \"qty\" : 2 } }");
        }

        [Fact]
        public async Task AllOtherFields_SplitValuesWinOnClashes_InPlace()
        {
            // The destination name "sku" clashes with the parent's "sku": the split value replaces it in place.
            Outputs(await Run(Params("items", "allOtherFields", destination: "sku"), Doc("{ \"sku\": \"parent\", \"items\": [\"child\"], \"z\": 1 }")))
                .Should().Equal("{ \"sku\" : \"child\", \"z\" : 1 }");
        }

        [Fact]
        public async Task Example3_SeveralFields_ZippedByPosition()
        {
            Outputs(await Run(Params("a, b"), Doc("{ \"a\": [1,2,3], \"b\": [\"x\",\"y\"] }"))).Should().Equal(
                "{ \"a\" : 1, \"b\" : \"x\" }", "{ \"a\" : 2, \"b\" : \"y\" }", "{ \"a\" : 3 }");
        }

        [Fact]
        public async Task Example4_MissingField_GivesNoItems_AndSucceeds()
        {
            var result = await Run(Params("tags"), Doc("{ \"name\": \"n\" }"));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task Example4_NoInputItems_Succeeds()
        {
            (await Run(Params("tags"))).OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task Example5_TenThousandAndFiveItems_NoLimit()
        {
            var inputs = Enumerable.Range(0, 2001).Select(_ => (BsonValue)Doc("{ \"items\": [1,2,3,4,5] }")).ToArray();

            var result = await Run(Params("items"), inputs);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().HaveCount(10_005);
        }

        [Theory]
        [InlineData("", "noOtherFields", "", "", "Fields To Split Out is required.")]
        [InlineData(" , ,", "noOtherFields", "", "", "Fields To Split Out is required.")]
        [InlineData("lines", "selectedOtherFields", " ", "", "Fields To Include is required.")]
        public async Task Example5_InvalidConfiguration_FailsWithNoItems(string field, string include, string fieldsToInclude, string destination, string message)
        {
            var result = await Run(Params(field, include, fieldsToInclude, destination), Order);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be(message);
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task Example6_ScalarsAndObjects()
        {
            Outputs(await Run(Params("v"), Doc("{ \"v\": 5 }"))).Should().Equal("{ \"v\" : 5 }");
            Outputs(await Run(Params("m"), Doc("{ \"m\": { \"x\": 1, \"y\": 2 } }"))).Should().Equal("{ \"m\" : 1 }", "{ \"m\" : 2 }");
            Outputs(await Run(Params("n"), Doc("{ \"n\": null }"))).Should().Equal("{ \"n\" : null }");
        }

        [Fact]
        public async Task SelectedOtherFields_CopiesOnlyPresentPaths()
        {
            var input = Doc("{ \"orderId\": 7, \"customer\": { \"name\": \"c\" }, \"lines\": [ { \"sku\": \"A\" } ] }");

            Outputs(await Run(Params("lines", "selectedOtherFields", "orderId, customer.name, missing"), input))
                // Object elements are spread only with "no other fields"; here they stay under the field name (n8n).
                .Should().Equal("{ \"lines\" : { \"sku\" : \"A\" }, \"orderId\" : 7, \"customer.name\" : \"c\" }");
        }

        [Fact]
        public async Task DotNotation_ReadsNested_AndCanBeDisabled()
        {
            var input = Doc("{ \"a\": { \"b\": [1, 2] }, \"a.b\": [9] }");

            Outputs(await Run(Params("a.b"), input)).Should().Equal("{ \"a.b\" : 1 }", "{ \"a.b\" : 2 }");
            Outputs(await Run(Params("a.b", disableDotNotation: true), input)).Should().Equal("{ \"a.b\" : 9 }");
        }

        [Fact]
        public async Task JsonPrefixes_AreStripped()
        {
            Outputs(await Run(Params("$json.output.lines"), Order)).Should().HaveCount(2);
            Outputs(await Run(Params("$json.lines"), Order)).Should().HaveCount(2);
        }

        // ---------- n8n 1.1+: destination names with dots are paths ----------

        [Fact]
        public async Task DottedDestination_IsWrittenAsANestedPath()
        {
            var result = await Run(Params("lines", destination: "order.line"), Order);

            Outputs(result).Should().Equal(
                "{ \"order\" : { \"line\" : { \"sku\" : \"A\", \"qty\" : 1 } } }",
                "{ \"order\" : { \"line\" : { \"sku\" : \"B\", \"qty\" : 2 } } }");
        }

        [Fact]
        public async Task DottedDestination_WithAllOtherFields_GoesInsideTheExistingDocument()
        {
            var input = Doc("{ \"id\": 7, \"order\": { \"ref\": \"X\", \"lines\": [ { \"sku\": \"A\" } ] } }");

            var result = await Run(Params("order.lines", include: "allOtherFields", destination: "order.line"), input);

            Outputs(result).Should().Equal("{ \"id\" : 7, \"order\" : { \"ref\" : \"X\", \"line\" : { \"sku\" : \"A\" } } }");
        }

        [Fact]
        public async Task NestedFieldWithoutDestination_WithAllOtherFields_IsATopLevelKey_AsInN8n()
        {
            var input = Doc("{ \"id\": 7, \"order\": { \"ref\": \"X\", \"lines\": [ { \"sku\": \"A\" } ] } }");

            var result = await Run(Params("order.lines", include: "allOtherFields"), input);

            Outputs(result).Should().Equal("{ \"id\" : 7, \"order\" : { \"ref\" : \"X\" }, \"order.lines\" : { \"sku\" : \"A\" } }");
        }

        [Fact]
        public async Task DottedDestination_WithDotNotationDisabled_IsOneLiteralKey()
        {
            var result = await Run(Params("lines", destination: "order.line", disableDotNotation: true), Order);

            Outputs(result).First().Should().Be("{ \"order.line\" : { \"sku\" : \"A\", \"qty\" : 1 } }");
        }

        [Fact]
        public async Task SeveralFields_WithDottedDestinations_PairByPosition()
        {
            var input = Doc("{ \"names\": [\"Ali\", \"Mina\"], \"scores\": [90] }");

            var result = await Run(Params("names, scores", destination: "person.name, person.score"), input);

            Outputs(result).Should().Equal(
                "{ \"person\" : { \"name\" : \"Ali\", \"score\" : 90 } }",
                "{ \"person\" : { \"name\" : \"Mina\" } }");
        }

        [Fact]
        public async Task EachFieldHasItsOwnOutputName_AnEmptyOneKeepsTheFieldName()
        {
            var input = Doc("{ \"names\": [\"Ali\"], \"scores\": [90] }");

            var result = await Run(Params("names, scores", destination: "person,"), input);

            // An output field name is written after the plain fields, as in n8n.
            Outputs(result).Should().Equal("{ \"scores\" : 90, \"person\" : \"Ali\" }");
        }

        [Fact]
        public async Task ReadsTheSavedListShape()
        {
            var parameters = Doc("""
                { "fields": [ { "fieldName": "lines", "outputFieldName": "line" }, null, { "fieldName": " " } ],
                  "include": "selectedOtherFields", "fieldsToInclude": [ "orderId", "", null ], "disableDotNotation": false }
                """);

            var result = await Run(parameters, Order);

            Outputs(result).Should().Equal(
                "{ \"orderId\" : 7, \"line\" : { \"sku\" : \"A\", \"qty\" : 1 } }",
                "{ \"orderId\" : 7, \"line\" : { \"sku\" : \"B\", \"qty\" : 2 } }");
        }

        [Fact]
        public async Task AFieldDraggedFromTheInputPanel_IsReadAsItsPath()
        {
            var result = await Run(Params("{{$json.output.lines}}"), Order);

            Outputs(result).Should().HaveCount(2);
        }

        [Fact]
        public async Task SeveralInputs_KeepInputOrder()
        {
            var result = await Run(Params("v"), Doc("{ \"v\": [1, 2] }"), Doc("{ \"v\": [3] }"));

            Outputs(result).Should().Equal("{ \"v\" : 1 }", "{ \"v\" : 2 }", "{ \"v\" : 3 }");
            result.OutputItems.Select(o => o.ParentItemIds!.Single()).Should().Equal("x1", "x1", "x2");
        }

        [Fact]
        public async Task NonDocumentOutput_CountsAsNotFound()
        {
            var result = await Run(Params("v"), new BsonArray { 1, 2 }, BsonNull.Value, Doc("{ \"v\": [1] }"));

            Outputs(result).Should().Equal("{ \"v\" : 1 }");
        }

        [Fact]
        public async Task UnknownInclude_FailsTheStep()
        {
            var result = await Run(Params("lines", "weird"), Order);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("include");
            result.OutputItems.Should().BeEmpty();
        }

        [Theory]
        [InlineData("noOtherFields", 2)]
        [InlineData("NoOtherFields", 2)]
        [InlineData("allOtherFields", 2)]
        public async Task Include_ReadsTheSavedNames(string include, int count)
        {
            Outputs(await Run(Params("lines", include), Order)).Should().HaveCount(count);
        }

        [Fact]
        public async Task OutputItems_KeepTheParametersAsSaved()
        {
            var parameters = Params("lines", "allOtherFields");

            var result = await Run(parameters, Order);

            result.OutputItems.Should().OnlyContain(o => o.Data.Parameters == parameters);
            result.OutputItems[0].Data.Parameters["include"].AsString.Should().Be("allOtherFields");
        }

        [Fact]
        public async Task SplitItems_AreIndependentCopies()
        {
            var input = Doc("{ \"lines\": [ { \"tags\": [\"a\"] } ] }");
            var result = await Run(Params("lines", "allOtherFields", destination: "line"), input);

            ((BsonDocument)result.OutputItems[0].Data.Output)["line"]["tags"].AsBsonArray.Add("b");

            input["lines"][0]["tags"].AsBsonArray.Should().HaveCount(1);
        }

        // ---------- C13: pinned in Test mode it still runs ----------

        [Fact]
        public void Pinned_InTestMode_RunsAndReplacesOutputs()
        {
            var execution = (WorkflowExecutionEntity)RuntimeHelpers.GetUninitializedObject(typeof(WorkflowExecutionEntity));
            execution.ExecutionMode = WorkflowExecutionMode.Test;
            var node = (NodeEntity)RuntimeHelpers.GetUninitializedObject(typeof(NodeEntity));
            node.Category = "transform";
            node.Type = "splitOut";
            node.PinData = new BsonArray { new BsonDocument("x", 1) };

            var (postProcess, runInstead) = WorkflowEngineService.PinBehavior(execution, node);

            runInstead.Should().BeNull();
            postProcess.Should().NotBeNull();
        }

        // ---------- C14: registry and DI ----------

        [Fact]
        public void DeclaresOnlyV2_AndIsRegistered()
        {
            INodeExecutor executor = new TransformSplitOutV2Node();
            executor.NodeType.Should().Be("splitOut");
            executor.Versions.Should().Equal("v2");

            var registry = new NodeExecutorRegistry([executor]);
            registry.Resolve("splitOut", "v2").Executor.Should().BeSameAs(executor);
            var act = () => registry.Resolve("splitOut", "v1");
            act.Should().Throw<InvalidOperationException>();

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            global::Workflow.DomainService.WorkflowExecutionServiceCollectionExtensions.AddWorkflowExecutionEngine(services);
            services.Where(d => d.ServiceType == typeof(INodeExecutor)).Select(d => d.ImplementationType)
                .Should().ContainSingle(t => t == typeof(TransformSplitOutV2Node));
        }
    }
}
