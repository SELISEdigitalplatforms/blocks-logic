using System.Net;
using System.Text;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionDataV1;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Pins tenant routing on the data-action node: GraphQL gateway calls must use
    /// <see cref="NodeExecutionContext.TenantId"/>, not ambient <see cref="BlocksContext"/>.
    /// Ambient context is unset for webhook <c>authType=none</c> (and blocksAuthentication),
    /// which used to send an empty X-Blocks-Key and produce IAM invalid_client.
    /// </summary>
    public class ActionDataV1NodeTests : IDisposable
    {
        private const string ContextTenantId = "tenant-from-context";
        private const string AmbientTenantId = "tenant-from-ambient";

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task ClientCredential_uses_context_tenant_when_ambient_BlocksContext_is_null()
        {
            TestBlocksContext.Clear();
            BlocksContext.GetContext().Should().BeNull();

            var captured = await RunGetDataWithClientCredentialAsync();

            captured.TokenTenantId.Should().Be(ContextTenantId);
            captured.BlocksKey.Should().Be(ContextTenantId);
            captured.Authorization.Should().Be("Bearer token-abc");
        }

        [Fact]
        public async Task ClientCredential_uses_context_tenant_not_ambient_BlocksContext_tenant()
        {
            TestBlocksContext.Set(AmbientTenantId);

            var captured = await RunGetDataWithClientCredentialAsync();

            captured.TokenTenantId.Should().Be(ContextTenantId);
            captured.BlocksKey.Should().Be(ContextTenantId);
        }

        [Fact]
        public async Task Gateway_request_sets_x_blocks_key_from_context_without_client_credential()
        {
            TestBlocksContext.Clear();

            var captured = await RunGetDataAsync(clientCredential: false);

            captured.TokenTenantId.Should().BeNull();
            captured.BlocksKey.Should().Be(ContextTenantId);
            captured.Authorization.Should().BeNull();
        }

        private static async Task<CapturedRequest> RunGetDataWithClientCredentialAsync()
            => await RunGetDataAsync(clientCredential: true);

        private static async Task<CapturedRequest> RunGetDataAsync(bool clientCredential)
        {
            var handler = new StubHandler(HttpStatusCode.OK,
                """{"data":{"getTasks":{"items":[{"ItemId":"1"}],"totalCount":1}}}""");
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(handler, disposeHandler: false));

            string? tokenTenantId = null;
            var tokenService = new Mock<IClientCredentialTokenService>();
            tokenService
                .Setup(s => s.GetTokenAsync(It.IsAny<ClientCredential>(), It.IsAny<string>()))
                .Callback<ClientCredential, string>((_, tenantId) => tokenTenantId = tenantId)
                .ReturnsAsync("token-abc");

            var node = new ActionDataV1Node(
                factory.Object,
                tokenService.Object,
                new Mock<IWorkflowAuthService>().Object,
                new ConfigurationBuilder().Build(),
                NullLogger<ActionDataV1Node>.Instance);

            var result = await node.RunAsync(Context(clientCredential));
            result.IsSuccess.Should().BeTrue();

            handler.Request.Should().NotBeNull();
            handler.Request!.RequestUri!.AbsolutePath.Should().Be("/api/gateway");

            return new CapturedRequest(
                tokenTenantId,
                handler.Request.Headers.TryGetValues("x-blocks-key", out var keys) ? keys.Single() : null,
                handler.Request.Headers.Authorization?.ToString());
        }

        private static NodeExecutionContext Context(bool clientCredential)
        {
            var parameters = new BsonDocument
            {
                { "CollectionName", "Tasks" },
                { "SchemaName", "Task" },
                { "ActionType", "getData" },
                { "ApiBaseUrl", "https://api.example.com" },
                { "AuthenticationType", clientCredential ? "clientCredential" : "" },
                { "ClientId", clientCredential ? "client-1" : "" },
                { "ClientSecret", clientCredential ? "s3cr3t" : "" },
            };

            var item = new WorkflowItemExecutionEntity
            {
                Id = "item-1",
                WorkflowExecutionId = "exec-1",
                TenantId = ContextTenantId,
                NodeId = "upstream",
                NodeExecutionId = "ne-1",
                NodeName = "Upstream",
                Branch = "source",
                ParentItemIds = new List<string>(),
                AncestorMap = new Dictionary<string, string>(),
                Data = new NodeOutputItemData { Output = new BsonDocument() },
            };

            return new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                WorkflowId = "wf-1",
                NodeId = "data-action",
                TenantId = ContextTenantId,
                Parameters = parameters,
                InputItems = new List<WorkflowItemExecutionEntity> { item },
                IterationCount = 1,
                WorkflowContext = new BsonDocument(),
            };
        }

        private sealed record CapturedRequest(string? TokenTenantId, string? BlocksKey, string? Authorization);

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            public HttpRequestMessage? Request { get; private set; }

            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
