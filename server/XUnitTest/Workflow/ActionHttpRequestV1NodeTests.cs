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
            public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
                IReadOnlyCollection<string> names,
                string tenantId,
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
        public async Task RunAsync_NonJsonBody_FailsTheNodeKeepingAnErrorItem()
        {
            var result = await RunWithResponse("not-json");

            // Fail fast: the node fails (the engine stops the workflow here); the error item stays on
            // the result so the execution record shows what failed.
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
            result.OutputItems.Should().ContainSingle();
            var output = result.OutputItems[0].Data.Output.AsBsonDocument;
            output["error"].AsBoolean.Should().BeTrue();
            output["message"].AsString.Should().Be(result.ErrorMessage);
        }

        private sealed class CountingHandler : HttpMessageHandler
        {
            private readonly Func<int, HttpResponseMessage> _respond;
            public int Calls { get; private set; }

            public CountingHandler(Func<int, HttpResponseMessage> respond) => _respond = respond;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(_respond(Calls));
            }
        }

        private static (ActionHttpRequestV1Node node, NodeExecutionContext context) MultiItemRun(HttpMessageHandler handler, int items)
        {
            var input = Enumerable.Range(1, items).Select(i =>
            {
                var item = Item();
                item.Id = $"item-{i}";
                return item;
            }).ToList();
            var context = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument { { "httpMethod", "GET" }, { "url", "https://example.test/items" } },
                InputItems = input,
                IterationCount = input.Count,
                WorkflowContext = new BsonDocument(),
            };
            var node = new ActionHttpRequestV1Node(
                new TestHttpClientFactory(handler),
                Mock.Of<IWorkflowAuthService>(),
                Mock.Of<IClientCredentialTokenService>(),
                NullLogger<ActionHttpRequestV1Node>.Instance);
            return (node, context);
        }

        [Fact]
        public async Task RunAsync_ItemFails_LaterItemsAreNotRequested()
        {
            var handler = new CountingHandler(call => call == 2
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
            var (node, context) = MultiItemRun(handler, items: 3);

            var result = await node.RunAsync(context);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
            handler.Calls.Should().Be(2, "item 3 must never be requested once item 2 failed");
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Data.Output.AsBsonDocument.Contains("error").Should().BeFalse();
            result.OutputItems[1].Data.Output["error"].AsBoolean.Should().BeTrue();
            result.OutputItems[1].ParentItemIds.Should().Equal("item-2");
        }

        [Fact]
        public async Task RunAsync_CancelledToken_PropagatesInsteadOfFailingTheNode()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var handler = new CountingHandler(_ => throw new OperationCanceledException(cts.Token));
            var (node, context) = MultiItemRun(handler, items: 1);
            context.CancellationToken = cts.Token;

            var act = () => node.RunAsync(context);

            await act.Should().ThrowAsync<OperationCanceledException>();
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
