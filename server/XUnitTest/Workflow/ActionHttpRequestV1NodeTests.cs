using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Proxy.DomainService.Services;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionHttpRequestV1;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    public class ActionHttpRequestV1NodeTests
    {
        private sealed class FakeVariableResolver : IProxyVariableResolver
        {
            public Task<IReadOnlyDictionary<string, string>> LookupIdsAsync(
                IReadOnlyCollection<string> names, string tenantId, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyDictionary<string, string>>(
                    names.ToDictionary(n => n, n => "id-" + n, StringComparer.Ordinal));

            public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
                IReadOnlyCollection<string> names,
                string tenantId,
                IReadOnlyDictionary<string, string>? knownIds = null,
                CancellationToken ct = default)
            {
                IReadOnlyDictionary<string, string> values = names
                    .ToDictionary(name => name, name => name == "bbb" ? "resolved-bbb" : "");

                return Task.FromResult(values);
            }
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            private readonly string _responseBody;

            public CaptureHandler(string responseBody = "{\"ok\":true}")
            {
                _responseBody = responseBody;
            }

            public string? Content { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Content = request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseBody)
                };
            }
        }

        private sealed class TestHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;

            public TestHttpClientFactory(HttpMessageHandler handler)
            {
                _handler = handler;
            }

            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private static WorkflowItemExecutionEntity Item() => new()
        {
            Id = "item-1",
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            NodeId = "node-1",
            NodeExecutionId = "ne-1",
            NodeName = "Trigger",
            Branch = "source",
            ParentItemIds = new List<string>(),
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = new BsonDocument() },
        };

        [Fact]
        public async Task RunAsync_LowercaseHaveBody_SendsResolvedVariableBody()
        {
            var handler = new CaptureHandler();
            var services = new ServiceCollection()
                .AddSingleton<IProxyVariableResolver, FakeVariableResolver>()
                .BuildServiceProvider();
            var parameters = new BsonDocument
            {
                { "httpMethod", "POST" },
                { "url", "https://example.test/post" },
                { "haveQueryParameters", false },
                { "queryParameters", new BsonDocument() },
                { "haveHeaders", false },
                { "headers", new BsonDocument() },
                { "haveBody", false },
                { "bodyContentType", "json" },
                { "body", "{\"data\":\"{{$VAR.bbb}}\"}" },
                { "havebody", true },
            };
            var context = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = new List<WorkflowItemExecutionEntity> { Item() },
                IterationCount = 1,
                WorkflowContext = new BsonDocument(),
                ServiceProvider = services,
            };
            var node = new ActionHttpRequestV1Node(
                new TestHttpClientFactory(handler),
                Mock.Of<IWorkflowAuthService>(),
                Mock.Of<IClientCredentialTokenService>(),
                NullLogger<ActionHttpRequestV1Node>.Instance);

            var result = await node.RunAsync(context);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            handler.Content.Should().Be("{\"data\":\"resolved-bbb\"}");
        }

        [Fact]
        public async Task RunAsync_RootJsonArray_SplitsIntoOneItemPerElement()
        {
            var result = await RunWithResponse("[{\"id\":1},{\"id\":2}]");

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Data.Output["id"].ToInt32().Should().Be(1);
            result.OutputItems[1].Data.Output["id"].ToInt32().Should().Be(2);
        }

        [Theory]
        [InlineData("47092838")]
        [InlineData("true")]
        [InlineData("\"hello\"")]
        public async Task RunAsync_RootJsonScalar_StoresThatValue(string responseBody)
        {
            var result = await RunWithResponse(responseBody);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().ContainSingle();
            var output = result.OutputItems[0].Data.Output;
            output.IsBsonDocument.Should().BeFalse();
            output.ToString().Should().Be(responseBody == "\"hello\"" ? "hello" : responseBody);
        }

        [Fact]
        public async Task RunAsync_NonJsonBody_ProducesErrorItem()
        {
            var result = await RunWithResponse("not-json");

            result.OutputItems.Should().ContainSingle();
            var output = result.OutputItems[0].Data.Output.AsBsonDocument;
            output["error"].AsBoolean.Should().BeTrue();
            output.Contains("message").Should().BeTrue();
        }

        // ----- Body is a JSON template (PKG-20 / PKG-23) ------------------------------------

        private static WorkflowItemExecutionEntity ItemWith(string id, BsonDocument output)
        {
            var item = Item();
            item.Id = id;
            item.Data = new NodeOutputItemData { Output = output };
            return item;
        }

        private static async Task<(NodeExecutionResult Result, List<string?> Sent)> RunWithBody(
            string bodyContentType, string body, params WorkflowItemExecutionEntity[] items)
        {
            var sent = new List<string?>();
            var handler = new RecordingHandler(sent);
            var context = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument
                {
                    { "httpMethod", "POST" },
                    { "url", "https://example.test/post" },
                    { "bodyContentType", bodyContentType },
                    { "body", body },
                    { "havebody", true },
                },
                InputItems = items.Length > 0 ? items.ToList() : new List<WorkflowItemExecutionEntity> { Item() },
                IterationCount = items.Length > 0 ? items.Length : 1,
                WorkflowContext = new BsonDocument(),
            };
            var node = new ActionHttpRequestV1Node(
                new TestHttpClientFactory(handler),
                Mock.Of<IWorkflowAuthService>(),
                Mock.Of<IClientCredentialTokenService>(),
                NullLogger<ActionHttpRequestV1Node>.Instance);
            return (await node.RunAsync(context), sent);
        }

        private sealed class RecordingHandler(List<string?> sent) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                sent.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
            }
        }

        [Fact]
        public async Task RunAsync_JsonBodyInvalidAfterFill_FailsTheStepAndSendsNothing()
        {
            // Was: an error item on a successful step.
            var (result, sent) = await RunWithBody("json", "{\"a\": {{$json.name}} {{$json.name}}}",
                ItemWith("i1", new BsonDocument("name", "Bob")));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().StartWith("Body is not valid JSON after filling in values:")
                .And.Contain("(line 1, position").And.EndWith("(item 1)");
            result.ErrorMessage.Should().NotContain("Bob", "the filled body can carry secrets and is never echoed");
            sent.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_JsonBodyValuesWithQuotes_AreEscaped()
        {
            var (result, sent) = await RunWithBody("json",
                "{\"slug\": \"{{$json.output.slug}}\", \"name\": {{$json.name}}, \"u\": \"{{$json.u}}\"}",
                ItemWith("i1", new BsonDocument { { "slug", "\"my-post\"" }, { "name", "Bob \"B\" Smith" }, { "u", "日本 ✓" } }));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            using var doc = System.Text.Json.JsonDocument.Parse(sent.Single()!);
            doc.RootElement.GetProperty("slug").GetString().Should().Be("\"my-post\"");
            doc.RootElement.GetProperty("name").GetString().Should().Be("Bob \"B\" Smith");
            doc.RootElement.GetProperty("u").GetString().Should().Be("日本 ✓");
        }

        [Fact]
        public async Task RunAsync_TextBody_IsFilledRawAsBefore()
        {
            var (result, sent) = await RunWithBody("text", "say \"{{$json.q}}\"",
                ItemWith("i1", new BsonDocument("q", "a\"b")));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            sent.Single().Should().Be("say \"a\"b\"");
        }

        [Fact]
        public async Task RunAsync_BlankJsonBody_SendsNoBody()
        {
            var (result, sent) = await RunWithBody("json", "   ");

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            sent.Single().Should().BeNull();
        }

        [Fact]
        public async Task RunAsync_InvalidJsonBodyOnALaterItem_FailsTheStepAndKeepsEarlierItems()
        {
            var (result, sent) = await RunWithBody("json", "{\"a\": 1{{$json.v}}}",
                ItemWith("i1", new BsonDocument("v", 5)),
                ItemWith("i2", new BsonDocument("v", "x")));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().EndWith("(item 2)");
            sent.Should().ContainSingle().Which.Should().Be("{\"a\": 15}");
            result.OutputItems.Should().ContainSingle();
        }

        private static async Task<NodeExecutionResult> RunWithResponse(string responseBody)
        {
            var services = new ServiceCollection()
                .AddSingleton<IProxyVariableResolver, FakeVariableResolver>()
                .BuildServiceProvider();
            var context = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument
                {
                    { "httpMethod", "GET" },
                    { "url", "https://example.test/items" },
                },
                InputItems = new List<WorkflowItemExecutionEntity> { Item() },
                IterationCount = 1,
                WorkflowContext = new BsonDocument(),
                ServiceProvider = services,
            };
            var node = new ActionHttpRequestV1Node(
                new TestHttpClientFactory(new CaptureHandler(responseBody)),
                Mock.Of<IWorkflowAuthService>(),
                Mock.Of<IClientCredentialTokenService>(),
                NullLogger<ActionHttpRequestV1Node>.Instance);

            return await node.RunAsync(context);
        }
    }
}
