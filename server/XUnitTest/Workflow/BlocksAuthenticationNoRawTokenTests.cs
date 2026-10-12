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
using Workflow.DomainService.Nodes.ActionHttpRequestV1;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// WS-1: with "Blocks Authentication", the HTTP Request and Data nodes send only a delegated token.
    /// They never forward the caller's raw BlocksContext.OAuthToken and never send without Authorization:
    /// no delegated token → the item fails with <see cref="NoDelegatedTokenException.DefaultMessage"/>.
    /// </summary>
    public class BlocksAuthenticationNoRawTokenTests : IDisposable
    {
        private const string RawCallerToken = "raw-caller-token";

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        /// <summary>Ambient context of an API request: the caller's own JWT is in OAuthToken.</summary>
        private static void SetCallerContextWithRawToken()
        {
            var create = typeof(BlocksContext)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .First(m => m.GetParameters().Length >= 15);
            var args = new object?[create.GetParameters().Length];
            object?[] known =
            {
                "tenant-1", Array.Empty<string>(), "user-1", true, string.Empty, "org-1",
                DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                "testuser", string.Empty, "Test User", RawCallerToken, "tenant-1", string.Empty,
            };
            for (var i = 0; i < args.Length; i++)
            {
                var p = create.GetParameters()[i];
                args[i] = i < known.Length ? known[i] : (p.HasDefaultValue ? p.DefaultValue : null);
            }
            var context = (BlocksContext)create.Invoke(null, args)!;
            BlocksContext.SetContext(context, true);
            BlocksContext.GetContext()!.OAuthToken.Should().Be(RawCallerToken);
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            private readonly string _body;
            public List<HttpRequestMessage> Requests { get; } = new();
            public CaptureHandler(string body) => _body = body;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Requests.Add(request);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                });
            }
        }

        private static IHttpClientFactory Factory(HttpMessageHandler handler)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(handler, disposeHandler: false));
            return factory.Object;
        }

        private static IWorkflowAuthService Auth(string? delegatedToken)
        {
            var auth = new Mock<IWorkflowAuthService>();
            auth.Setup(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(delegatedToken);
            return auth.Object;
        }

        private static WorkflowItemExecutionEntity Item() => new()
        {
            Id = "item-1", WorkflowExecutionId = "exec-1", TenantId = "tenant-1", NodeId = "t",
            NodeExecutionId = "ne-1", NodeName = "Trigger", Branch = "source",
            ParentItemIds = new List<string>(), AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = new BsonDocument() },
        };

        private static NodeExecutionContext Context(BsonDocument parameters) => new()
        {
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            Parameters = parameters,
            InputItems = new List<WorkflowItemExecutionEntity> { Item() },
            IterationCount = 1,
            WorkflowContext = new BsonDocument(),
        };

        private static void ShouldBeNoTokenErrorItem(NodeExecutionResult result)
        {
            var output = result.OutputItems.Should().ContainSingle().Subject.Data.Output.AsBsonDocument;
            output["error"].AsBoolean.Should().BeTrue();
            output["message"].AsString.Should().Be(NoDelegatedTokenException.DefaultMessage);
        }

        // ---------- HTTP Request node ----------

        private static BsonDocument HttpParams(string authenticationType, bool useBlocksAuthorization = false, BsonDocument? headers = null) => new()
        {
            { "httpMethod", "GET" },
            { "url", "https://author-chosen.example/x" },
            { "authenticationType", authenticationType },
            { "useBlocksAuthorization", useBlocksAuthorization },
            { "haveHeaders", headers != null },
            { "headers", headers ?? new BsonDocument() },
        };

        private static async Task<(NodeExecutionResult result, CaptureHandler handler)> RunHttp(BsonDocument parameters, string? delegatedToken)
        {
            var handler = new CaptureHandler("""{"ok":true}""");
            var node = new ActionHttpRequestV1Node(Factory(handler), Auth(delegatedToken),
                Mock.Of<IClientCredentialTokenService>(), NullLogger<ActionHttpRequestV1Node>.Instance);
            return (await node.RunAsync(Context(parameters)), handler);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Http_no_delegated_token_fails_item_and_sends_nothing_even_with_raw_caller_token(string? delegated)
        {
            SetCallerContextWithRawToken();

            var (result, handler) = await RunHttp(HttpParams("blocksAuthentication"), delegated);

            ShouldBeNoTokenErrorItem(result);
            handler.Requests.Should().BeEmpty("the raw caller token must never reach an author-chosen URL");
        }

        [Fact]
        public async Task Http_no_delegated_token_and_no_context_fails_item_instead_of_sending_unauthenticated()
        {
            TestBlocksContext.Clear();

            var (result, handler) = await RunHttp(HttpParams("blocksAuthentication"), null);

            ShouldBeNoTokenErrorItem(result);
            handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Http_legacy_UseBlocksAuthorization_flag_follows_the_same_rule()
        {
            SetCallerContextWithRawToken();

            var (result, handler) = await RunHttp(HttpParams("", useBlocksAuthorization: true), null);

            ShouldBeNoTokenErrorItem(result);
            handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Http_delegated_token_is_sent()
        {
            SetCallerContextWithRawToken();

            var (result, handler) = await RunHttp(HttpParams("blocksAuthentication"), "delegated-1");

            result.OutputItems.Should().ContainSingle().Which.Data.Output.AsBsonDocument.Contains("error").Should().BeFalse();
            handler.Requests.Should().ContainSingle().Which.Headers.Authorization!.ToString().Should().Be("Bearer delegated-1");
        }

        [Fact]
        public async Task Http_manual_authorization_header_wins_and_needs_no_delegated_token()
        {
            var (result, handler) = await RunHttp(
                HttpParams("blocksAuthentication", headers: new BsonDocument { { "Authorization", "Bearer manual" } }), null);

            result.OutputItems.Should().ContainSingle().Which.Data.Output.AsBsonDocument.Contains("error").Should().BeFalse();
            handler.Requests.Should().ContainSingle().Which.Headers.Authorization!.ToString().Should().Be("Bearer manual");
        }

        [Fact]
        public async Task Http_no_authentication_is_unchanged()
        {
            SetCallerContextWithRawToken();

            var (result, handler) = await RunHttp(HttpParams(""), null);

            result.OutputItems.Should().ContainSingle().Which.Data.Output.AsBsonDocument.Contains("error").Should().BeFalse();
            handler.Requests.Should().ContainSingle().Which.Headers.Authorization.Should().BeNull();
        }

        // ---------- Data node ----------

        private static async Task<(NodeExecutionResult result, CaptureHandler handler)> RunData(string authenticationType, string? delegatedToken)
        {
            var handler = new CaptureHandler("""{"data":{"getTasks":{"items":[{"ItemId":"1"}],"totalCount":1}}}""");
            var node = new ActionDataV1Node(Factory(handler), Mock.Of<IClientCredentialTokenService>(),
                Auth(delegatedToken), new ConfigurationBuilder().Build(), NullLogger<ActionDataV1Node>.Instance);
            var parameters = new BsonDocument
            {
                { "CollectionName", "Tasks" },
                { "SchemaName", "Task" },
                { "ActionType", "getData" },
                { "ApiBaseUrl", "https://api.example.com" },
                { "AuthenticationType", authenticationType },
            };
            return (await node.RunAsync(Context(parameters)), handler);
        }

        [Fact]
        public async Task Data_no_delegated_token_fails_item_and_sends_nothing_even_with_raw_caller_token()
        {
            SetCallerContextWithRawToken();

            var (result, handler) = await RunData("blocksAuthentication", null);

            ShouldBeNoTokenErrorItem(result);
            handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Data_no_delegated_token_and_no_context_fails_item()
        {
            TestBlocksContext.Clear();

            var (result, handler) = await RunData("blocksAuthentication", null);

            ShouldBeNoTokenErrorItem(result);
            handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Data_delegated_token_is_sent()
        {
            SetCallerContextWithRawToken();

            var (result, handler) = await RunData("blocksAuthentication", "delegated-1");

            result.IsSuccess.Should().BeTrue();
            handler.Requests.Should().ContainSingle().Which.Headers.Authorization!.ToString().Should().Be("Bearer delegated-1");
        }

        [Fact]
        public async Task Data_no_authentication_is_unchanged()
        {
            SetCallerContextWithRawToken();

            var (_, handler) = await RunData("", null);

            handler.Requests.Should().ContainSingle().Which.Headers.Authorization.Should().BeNull();
        }
    }
}
