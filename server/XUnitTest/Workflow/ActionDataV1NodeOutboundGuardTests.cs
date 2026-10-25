using System.Net;
using System.Text;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionDataV1;
using Workflow.DomainService.Nodes.ActionHttpRequestV1;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// The Data node uses the HTTP Request step's outbound guard: a Blocks token goes only to a
    /// trusted Blocks host, an untrusted host may not resolve to a private address, the guarded
    /// named client is used, and the 10 MB response cap applies.
    /// </summary>
    public sealed class ActionDataV1NodeOutboundGuardTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private const string TrustedBase = "https://api.seliseblocks.com";
        private const string UntrustedBase = "https://collector.example.com";
        private const string ClientCredentialMode = "clientCredential";
        private const string BlocksAuthenticationMode = "blocksAuthentication";
        private const string OkBody = """{"data":{"getTasks":{"items":[{"ItemId":"1"}],"totalCount":1}}}""";

        private readonly Mock<IClientCredentialTokenService> _tokens = new();
        private readonly Mock<IWorkflowAuthService> _auth = new();
        private readonly Mock<IHttpClientFactory> _factory = new();
        private CaptureHandler _handler = new(OkBody);

        public ActionDataV1NodeOutboundGuardTests()
        {
            TestBlocksContext.Clear();
            _tokens.Setup(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("cc-token");
            _auth.Setup(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("delegated-token");
            _factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_handler, disposeHandler: false));
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private ActionDataV1Node Node(WorkflowOutboundGuard? guard = null, IConfiguration? configuration = null) =>
            new(_factory.Object, _tokens.Object, _auth.Object, configuration ?? new ConfigurationBuilder().Build(),
                NullLogger<ActionDataV1Node>.Instance, guard ?? TestOutboundGuard.ResolvingTo());

        private static BsonDocument Params(string apiBaseUrl, string authenticationType) => new()
        {
            { "CollectionName", "Tasks" },
            { "SchemaName", "Task" },
            { "ActionType", "getData" },
            { "ApiBaseUrl", apiBaseUrl },
            { "AuthenticationType", authenticationType },
            { "ClientId", "client-1" },
            { "ClientSecret", "s3cr3t" },
        };

        private static NodeExecutionContext Context(BsonDocument parameters) => new()
        {
            WorkflowExecutionId = "exec-1",
            WorkflowId = "wf-1",
            NodeId = "data-action",
            TenantId = TenantId,
            Parameters = parameters,
            InputItems =
            [
                new WorkflowItemExecutionEntity
                {
                    Id = "item-1",
                    WorkflowExecutionId = "exec-1",
                    TenantId = TenantId,
                    NodeId = "upstream",
                    NodeExecutionId = "ne-1",
                    NodeName = "Upstream",
                    Branch = "source",
                    ParentItemIds = [],
                    AncestorMap = [],
                    Data = new NodeOutputItemData { Output = new BsonDocument() },
                },
            ],
            IterationCount = 1,
            WorkflowContext = new BsonDocument(),
        };

        private static void ShouldBeErrorItem(NodeExecutionResult result, string message)
        {
            var output = result.OutputItems.Should().ContainSingle().Subject.Data.Output.AsBsonDocument;
            output["error"].AsBoolean.Should().BeTrue();
            output["message"].AsString.Should().Be(message);
        }

        private void NoTokenMade()
        {
            _tokens.Verify(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _auth.Verify(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(ClientCredentialMode, UntrustedBase)]
        [InlineData(BlocksAuthenticationMode, UntrustedBase)]
        [InlineData(ClientCredentialMode, "https://seliseblocks.com.collector.example")]
        [InlineData(BlocksAuthenticationMode, "https://notseliseblocks.com")]
        public async Task Token_to_an_untrusted_host_fails_the_item_before_a_token_is_made(string mode, string apiBaseUrl)
        {
            var result = await Node().RunAsync(Context(Params(apiBaseUrl, mode)));

            ShouldBeErrorItem(result, BlocksTokenHostNotTrustedException.DefaultMessage);
            _handler.Requests.Should().BeEmpty();
            NoTokenMade();
        }

        [Theory]
        [InlineData(ClientCredentialMode, "Bearer cc-token")]
        [InlineData(BlocksAuthenticationMode, "Bearer delegated-token")]
        public async Task Token_to_a_trusted_host_is_sent_through_the_guarded_client(string mode, string expected)
        {
            var result = await Node().RunAsync(Context(Params(TrustedBase, mode)));

            result.OutputItems.Should().ContainSingle().Which.Data.Output.AsBsonDocument.Contains("error").Should().BeFalse();
            var request = _handler.Requests.Should().ContainSingle().Subject;
            request.RequestUri.Should().Be(new Uri(TrustedBase + "/api/gateway"));
            request.Headers.Authorization!.ToString().Should().Be(expected);
            _factory.Verify(f => f.CreateClient(WorkflowOutboundGuard.HttpClientName), Times.Once);
        }

        [Fact]
        public async Task Trusted_host_may_resolve_to_a_private_address()
        {
            var guard = TestOutboundGuard.ResolvingTo(IPAddress.Parse("10.1.2.3"));

            var result = await Node(guard).RunAsync(Context(Params("https://dev-data.blocksdevelopers.com", ClientCredentialMode)));

            result.OutputItems.Should().ContainSingle().Which.Data.Output.AsBsonDocument.Contains("error").Should().BeFalse();
            _handler.Requests.Should().ContainSingle();
        }

        [Theory]
        [InlineData("http://10.0.0.5")]
        [InlineData("http://169.254.169.254")]
        [InlineData("http://127.0.0.1:8080")]
        [InlineData("ftp://api.seliseblocks.com")]
        [InlineData("not a url")]
        public async Task Private_or_non_http_destination_is_refused_even_without_a_token(string apiBaseUrl)
        {
            var result = await Node().RunAsync(Context(Params(apiBaseUrl, "")));

            ShouldBeErrorItem(result, WorkflowOutboundBlockedException.DefaultMessage);
            _handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Untrusted_host_resolving_to_a_private_address_is_refused()
        {
            var guard = TestOutboundGuard.ResolvingTo(IPAddress.Parse("192.168.1.10"));

            var result = await Node(guard).RunAsync(Context(Params(UntrustedBase, "")));

            ShouldBeErrorItem(result, WorkflowOutboundBlockedException.DefaultMessage);
            _handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Untrusted_public_host_without_a_token_is_still_called()
        {
            var result = await Node().RunAsync(Context(Params(UntrustedBase, "")));

            result.OutputItems.Should().ContainSingle().Which.Data.Output.AsBsonDocument.Contains("error").Should().BeFalse();
            _handler.Requests.Should().ContainSingle().Which.Headers.Authorization.Should().BeNull();
        }

        [Fact]
        public async Task Response_over_the_cap_fails_the_item()
        {
            _handler = new CaptureHandler(OkBody, declaredLength: WorkflowOutboundGuard.MaxResponseBytes + 1);

            var result = await Node().RunAsync(Context(Params(TrustedBase, ClientCredentialMode)));

            ShouldBeErrorItem(result, ResponseTooLargeException.DefaultMessage);
        }

        [Fact]
        public async Task Configured_base_url_on_a_default_trusted_host_still_works()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = "https://dev-data.blocksdevelopers.com" })
                .Build();

            var result = await Node(configuration: configuration).RunAsync(Context(Params("", ClientCredentialMode)));

            result.OutputItems.Should().ContainSingle().Which.Data.Output.AsBsonDocument.Contains("error").Should().BeFalse();
            _handler.Requests.Should().ContainSingle().Which.Headers.Authorization!.ToString().Should().Be("Bearer cc-token");
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            private readonly string _body;
            private readonly long? _declaredLength;

            public CaptureHandler(string body, long? declaredLength = null)
            {
                _body = body;
                _declaredLength = declaredLength;
            }

            public List<HttpRequestMessage> Requests { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                var content = new StringContent(_body, Encoding.UTF8, "application/json");
                if (_declaredLength is not null)
                {
                    content.Headers.ContentLength = _declaredLength;
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
        }
    }
}
