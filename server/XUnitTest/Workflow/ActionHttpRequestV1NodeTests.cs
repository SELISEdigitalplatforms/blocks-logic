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
                    Content = new StringContent("{\"ok\":true}")
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
    }
}
