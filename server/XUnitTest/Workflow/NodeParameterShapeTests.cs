using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Proxy.DomainService.Services;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionProxy;
using Workflow.DomainService.Nodes.TransformSetFieldV1;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// A workflow built through the API can save node parameters in the wrong JSON shape (e.g. a
    /// Proxy node's pathParams as an array). The step used to fail with Newtonsoft's raw message,
    /// which did not name the field and can quote the value. Now the step fails with the field path
    /// and expected vs actual shape, never the value, for every node (the check is in the base class).
    /// </summary>
    public class NodeParameterShapeTests
    {
        private const string Secret = "sk_live_DO_NOT_ECHO";

        private static NodeExecutionContext Context(BsonDocument parameters) => new()
        {
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            Parameters = parameters,
            InputItems = new List<WorkflowItemExecutionEntity>
            {
                new()
                {
                    Id = "item-1", WorkflowExecutionId = "exec-1", TenantId = "tenant-1", NodeId = "n0",
                    NodeExecutionId = "ne-0", NodeName = "Start", Branch = "source",
                    Data = new NodeOutputItemData { Output = new BsonDocument() },
                },
            },
            IterationCount = 1,
            WorkflowContext = new BsonDocument(),
        };

        private static BsonDocument ProxyParameters(string field, BsonValue value) => new()
        {
            { "proxyId", "p1" }, { "slug", "orders" }, { "routeMethod", "GET" }, { "routePath", "orders/{id}" },
            { "haveQuery", true }, { "haveBody", true },
            { field, value },
        };

        private static async Task<(NodeExecutionResult Result, Mock<IProxyGatewayService> Gateway)> RunProxy(BsonDocument parameters)
        {
            var gateway = new Mock<IProxyGatewayService>();
            var node = new ActionProxyNode(gateway.Object, NullLogger<ActionProxyNode>.Instance);
            var result = await node.RunAsync(Context(parameters));
            return (result, gateway);
        }

        // ----- Proxy node: dictionary fields -----------------------------------------------

        [Theory]
        [InlineData("pathParams")]
        [InlineData("queryParams")]
        public async Task A_dictionary_field_sent_as_an_array_fails_the_step_naming_the_field(string field)
        {
            var (result, gateway) = await RunProxy(ProxyParameters(field, new BsonArray { Secret }));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be(
                $"Parameter '{field}' has the wrong shape: expected an object of text values, got an array.");
            result.ErrorMessage.Should().NotContain(Secret);
            gateway.Verify(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("pathParams")]
        [InlineData("queryParams")]
        public async Task A_dictionary_field_sent_as_a_number_or_text_fails_the_step(string field)
        {
            var (number, _) = await RunProxy(ProxyParameters(field, 42));
            number.ErrorMessage.Should().Be(
                $"Parameter '{field}' has the wrong shape: expected an object of text values, got a number.");

            var (text, _) = await RunProxy(ProxyParameters(field, Secret));
            text.ErrorMessage.Should().Be(
                $"Parameter '{field}' has the wrong shape: expected an object of text values, got text.");
            text.ErrorMessage.Should().NotContain(Secret);
        }

        [Theory]
        [InlineData("pathParams")]
        [InlineData("queryParams")]
        public async Task A_dictionary_field_sent_as_null_or_as_an_object_of_text_is_read(string field)
        {
            var (asNull, _) = await RunProxy(ProxyParameters(field, BsonNull.Value));
            asNull.ErrorMessage.Should().NotContain("wrong shape", "null is ignored and the field keeps its default");

            var (asObject, _) = await RunProxy(ProxyParameters(field, new BsonDocument("id", "7")));
            asObject.ErrorMessage.Should().NotContain("wrong shape");
        }

        [Fact]
        public async Task A_wrong_value_inside_a_dictionary_names_the_nested_path()
        {
            var (obj, _) = await RunProxy(ProxyParameters("pathParams",
                new BsonDocument { { "id", new BsonDocument("token", Secret) } }));
            obj.ErrorMessage.Should().Be("Parameter 'pathParams.id' has the wrong shape: expected text, got an object.");
            obj.ErrorMessage.Should().NotContain(Secret);

            var (arr, _) = await RunProxy(ProxyParameters("queryParams",
                new BsonDocument { { "page", "1" }, { "tags", new BsonArray { "a", "b" } } }));
            arr.ErrorMessage.Should().Be("Parameter 'queryParams.tags' has the wrong shape: expected text, got an array.");
        }

        // ----- Proxy node: string field -----------------------------------------------------

        [Fact]
        public async Task Body_sent_as_an_object_fails_the_step_without_echoing_it()
        {
            var (result, gateway) = await RunProxy(ProxyParameters("body", new BsonDocument("apiKey", Secret)));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("Parameter 'body' has the wrong shape: expected text, got an object.");
            result.ErrorMessage.Should().NotContain(Secret).And.NotContain("apiKey");
            gateway.Verify(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Body_sent_as_an_array_fails_the_step()
        {
            var (result, _) = await RunProxy(ProxyParameters("body", new BsonArray { 1, 2 }));

            result.ErrorMessage.Should().Be("Parameter 'body' has the wrong shape: expected text, got an array.");
        }

        [Fact]
        public async Task Body_sent_as_a_number_or_null_is_read_as_before()
        {
            // Newtonsoft turns a number into its text and ignores null; that stays (no change for
            // workflows that run today).
            var (number, _) = await RunProxy(ProxyParameters("body", 5));
            number.ErrorMessage.Should().NotContain("wrong shape");

            var (asNull, _) = await RunProxy(ProxyParameters("body", BsonNull.Value));
            asNull.ErrorMessage.Should().NotContain("wrong shape");
        }

        [Fact]
        public async Task A_field_name_in_another_case_is_reported_as_sent()
        {
            var (result, _) = await RunProxy(ProxyParameters("PathParams", new BsonArray()));

            result.ErrorMessage.Should().Be(
                "Parameter 'PathParams' has the wrong shape: expected an object of text values, got an array.");
        }

        // ----- Any node: lists, nested objects, numbers, bools --------------------------------

        [Fact]
        public async Task A_wrong_field_inside_a_list_item_names_the_index()
        {
            var node = new TransformSetFieldV1Node();
            var parameters = new BsonDocument
            {
                { "mode", "manual_mapping" },
                { "manualMappingFields", new BsonArray
                    {
                        new BsonDocument { { "key", "a" }, { "value", "1" }, { "type", "string" } },
                        new BsonDocument { { "key", new BsonArray { Secret } }, { "value", "2" } },
                    }
                },
            };

            var result = await node.RunAsync(Context(parameters));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be(
                "Parameter 'manualMappingFields[1].key' has the wrong shape: expected text, got an array.");
            result.ErrorMessage.Should().NotContain(Secret);
        }

        [Fact]
        public async Task A_list_field_sent_as_an_object_fails_the_step()
        {
            var node = new TransformSetFieldV1Node();
            var parameters = new BsonDocument { { "manualMappingFields", new BsonDocument("key", "a") } };

            var result = await node.RunAsync(Context(parameters));

            result.ErrorMessage.Should().Be(
                "Parameter 'manualMappingFields' has the wrong shape: expected an array of objects, got an object.");
        }

        private sealed class ShapeExecutor : NodeExecutorBase<ShapeParameters>
        {
            public override string NodeType => "shape-test";
            public override string Version => "v1";
            public bool Ran { get; private set; }

            protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, ShapeParameters? parameters)
            {
                Ran = true;
                return Task.FromResult(NodeExecutionResult.Empty());
            }
        }

        public sealed class ShapeParameters
        {
            public int Count { get; set; }
            public bool Enabled { get; set; }
            public Dictionary<string, int> Weights { get; set; } = new();
            public Inner Options { get; set; } = new();
        }

        public sealed class Inner
        {
            public List<string> Tags { get; set; } = new();
        }

        [Theory]
        [InlineData("count", "Parameter 'count' has the wrong shape: expected a number, got text.")]
        [InlineData("enabled", "Parameter 'enabled' has the wrong shape: expected true or false, got text.")]
        public async Task Text_that_cannot_be_a_number_or_bool_is_named_without_the_value(string field, string expected)
        {
            var executor = new ShapeExecutor();

            var result = await executor.RunAsync(Context(new BsonDocument(field, Secret)));

            result.ErrorMessage.Should().Be(expected);
            result.ErrorMessage.Should().NotContain(Secret);
            executor.Ran.Should().BeFalse();
        }

        [Fact]
        public async Task Text_that_is_a_number_or_bool_is_still_read()
        {
            var executor = new ShapeExecutor();

            var result = await executor.RunAsync(Context(new BsonDocument { { "count", "12" }, { "enabled", "true" } }));

            result.IsSuccess.Should().BeTrue();
            executor.Ran.Should().BeTrue();
        }

        [Fact]
        public async Task A_nested_object_and_its_list_are_named_by_path()
        {
            var executor = new ShapeExecutor();

            var result = await executor.RunAsync(Context(new BsonDocument("options",
                new BsonDocument("tags", new BsonDocument("a", "b")))));

            result.ErrorMessage.Should().Be(
                "Parameter 'options.tags' has the wrong shape: expected an array of text values, got an object.");
        }

        [Fact]
        public async Task A_dictionary_of_numbers_names_the_bad_entry()
        {
            var executor = new ShapeExecutor();

            var result = await executor.RunAsync(Context(new BsonDocument("weights",
                new BsonDocument { { "a", 1 }, { "b", Secret } })));

            result.ErrorMessage.Should().Be("Parameter 'weights.b' has the wrong shape: expected a number, got text.");
            result.ErrorMessage.Should().NotContain(Secret);
        }

        [Fact]
        public async Task An_object_field_sent_as_an_array_fails_the_step()
        {
            var executor = new ShapeExecutor();

            var result = await executor.RunAsync(Context(new BsonDocument("options", new BsonArray())));

            result.ErrorMessage.Should().Be("Parameter 'options' has the wrong shape: expected an object, got an array.");
        }

        [Fact]
        public void The_fallback_names_only_the_path()
        {
            NodeParameterShape.Fallback("a.b").Should().Be("Parameter 'a.b' has the wrong shape and could not be read.");
            NodeParameterShape.Fallback(null).Should().Be("The node parameters have the wrong shape and could not be read.");
        }

        [Fact]
        public void Good_parameters_have_no_mismatch()
        {
            NodeParameterShape.Describe(
                """{"pathParams":{"id":"1"},"queryParams":{},"body":"{}","haveQuery":true}""",
                typeof(ActionProxyParameters)).Should().BeNull();
        }
    }
}
