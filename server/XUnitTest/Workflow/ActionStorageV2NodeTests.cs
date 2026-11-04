using System.Net;
using System.Text;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionHttpRequestV1;
using Workflow.DomainService.Nodes.ActionStorageV2;
using Workflow.DomainService.Services;
using Workflow.DomainService.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC11: the Storage node's requests, output mapping, error mapping and secrecy.</summary>
    public sealed class ActionStorageV2NodeTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private const string Base = "https://blocksapi.dev.slsblx.com/data/v4";
        private const string SignedUrl = "https://storage.example/f?sig=SECRET";

        private const string FileBody = """
            {"isSuccess":true,"errors":null,"url":"https://storage.example/f?sig=SECRET","objectAccessLevel":"Organization",
             "itemId":"file-1","tags":["a"],"name":"inv.pdf","parentDirectoryID":"dir-1","createdDate":"2026-11-04T10:00:00Z",
             "createdBy":"user-7","sizeInBytes":182044,"verificationStatus":"Verified","downloadUrlExpiresAtUtc":"2026-11-04T10:15:00Z"}
            """;

        private readonly Mock<IClientCredentialTokenService> _tokens = new();
        private readonly Mock<IWorkflowAuthService> _auth = new();
        private readonly Mock<IHttpClientFactory> _factory = new();
        private readonly StubHandler _handler = new();

        public ActionStorageV2NodeTests()
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

        private ActionStorageV2Node Node(string? apiBaseUrl = Base, WorkflowOutboundGuard? guard = null) =>
            new(_factory.Object, _tokens.Object, _auth.Object,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = apiBaseUrl }).Build(),
                guard ?? TestOutboundGuard.ResolvingTo());

        private static BsonDocument Params(string operation, string fileId = "{{$json.fileId}}", string auth = "blocksAuthentication",
            string newName = "", bool permanent = false) => new()
        {
            { "operation", operation },
            { "authenticationType", auth },
            { "clientId", "client-1" },
            { "fileId", fileId },
            { "newName", newName },
            { "permanent", permanent },
        };

        private static NodeExecutionContext Context(BsonDocument parameters, params string[] fileIds)
        {
            var ids = fileIds.Length == 0 ? new[] { "file-1" } : fileIds;
            return new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                WorkflowId = "wf-1",
                NodeId = "storage",
                TenantId = TenantId,
                Parameters = parameters,
                InputItems = ids.Select((id, i) => new WorkflowItemExecutionEntity
                {
                    Id = $"item-{i + 1}",
                    WorkflowExecutionId = "exec-1",
                    TenantId = TenantId,
                    NodeId = "upstream",
                    NodeExecutionId = "ne-1",
                    NodeName = "Upstream",
                    Branch = "source",
                    ParentItemIds = [],
                    AncestorMap = [],
                    Data = new NodeOutputItemData { Output = new BsonDocument { { "fileId", id }, { "name", $"n{i}.pdf" } } },
                }).ToList(),
                IterationCount = ids.Length,
                WorkflowContext = new BsonDocument(),
            };
        }

        private static BsonDocument Single(NodeExecutionResult result) =>
            result.OutputItems.Should().ContainSingle().Subject.Data.Output.AsBsonDocument;

        private static void ShouldBeError(NodeExecutionResult result, string message)
        {
            var output = Single(result);
            output["error"].AsBoolean.Should().BeTrue();
            output["message"].AsString.Should().Be(message);
        }

        // ---------------------------------------------------------------- happy path

        [Fact]
        public async Task Get_calls_get_file_with_the_bearer_and_tenant_and_maps_the_file()   // H4, H8
        {
            _handler.Respond(HttpStatusCode.OK, FileBody);

            var result = await Node().RunAsync(Context(Params("get")));

            var request = _handler.Requests.Should().ContainSingle().Subject;
            (request.Method, request.Uri).Should().Be((HttpMethod.Get, new Uri(Base + "/files/get-file?FileId=file-1")));
            request.Authorization.Should().Be("Bearer delegated-token");
            request.BlocksKey.Should().Be(TenantId);
            var output = Single(result);
            output.Should().BeEquivalentTo(new BsonDocument
            {
                { "fileId", "file-1" }, { "name", "inv.pdf" }, { "size", 182044L }, { "folderId", "dir-1" },
                { "tags", new BsonArray { "a" } }, { "accessLevel", "Organization" }, { "createdBy", "user-7" },
                { "createdDate", "2026-11-04T10:00:00Z" }, { "verificationStatus", "Verified" }, { "url", SignedUrl },
                { "urlExpiresAt", "2026-11-04T10:15:00Z" },
            });
            result.IsSuccess.Should().BeTrue();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Delete_posts_the_file_and_the_permanent_flag(bool permanent)   // H5
        {
            _handler.Respond(HttpStatusCode.OK, """{"isSuccess":true,"errors":null}""");

            var result = await Node().RunAsync(Context(Params("delete", permanent: permanent)));

            var request = _handler.Requests.Single();
            (request.Method, request.Uri).Should().Be((HttpMethod.Post, new Uri(Base + "/files/delete-file")));
            request.Body.Should().Be($"{{\"FileId\":\"file-1\",\"Permanent\":{(permanent ? "true" : "false")}}}");
            Single(result).Should().BeEquivalentTo(new BsonDocument { { "fileId", "file-1" }, { "deleted", true }, { "permanent", permanent } });
        }

        [Fact]
        public async Task Rename_posts_the_resolved_name()   // H6
        {
            _handler.Respond(HttpStatusCode.OK, """{"fileId":"file-1"}""");

            var result = await Node().RunAsync(Context(Params("rename", newName: "renamed-{{$json.name}}")));

            var request = _handler.Requests.Single();
            (request.Method, request.Uri).Should().Be((HttpMethod.Post, new Uri(Base + "/files/rename-file")));
            request.Body.Should().Be("{\"FileId\":\"file-1\",\"Name\":\"renamed-n0.pdf\"}");
            Single(result).Should().BeEquivalentTo(new BsonDocument { { "fileId", "file-1" }, { "name", "renamed-n0.pdf" } });
        }

        [Fact]
        public async Task Each_item_gets_its_own_call_in_order_with_its_parent()   // H7
        {
            _handler.Respond(HttpStatusCode.OK, """{"isSuccess":true}""");

            var result = await Node().RunAsync(Context(Params("delete"), "a", "b", "c"));

            _handler.Requests.Select(r => r.Body).Should().Equal(
                "{\"FileId\":\"a\",\"Permanent\":false}", "{\"FileId\":\"b\",\"Permanent\":false}", "{\"FileId\":\"c\",\"Permanent\":false}");
            result.OutputItems.Select(o => o.ParentItemIds!.Single()).Should().Equal("item-1", "item-2", "item-3");
            _auth.Verify(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Client_credential_uses_the_client_token()   // H8
        {
            _handler.Respond(HttpStatusCode.OK, FileBody);

            await Node().RunAsync(Context(Params("get", auth: "clientCredential")));

            _handler.Requests.Single().Authorization.Should().Be("Bearer cc-token");
            _tokens.Verify(t => t.GetTokenAsync("client-1", TenantId), Times.Once);
            _auth.Verify(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Hidden_fields_are_ignored()   // C17
        {
            _handler.Respond(HttpStatusCode.OK, """{"isSuccess":true}""");

            var result = await Node().RunAsync(Context(Params("delete", newName: "ignored")));

            _handler.Requests.Single().Body.Should().NotContain("ignored");
            Single(result).Contains("name").Should().BeFalse();
        }

        // ---------------------------------------------------------------- refused before any call

        [Theory]
        [InlineData("")]
        [InlineData("{{$json.missing}}")]
        [InlineData("   ")]
        public async Task Blank_file_id_is_an_item_error_without_a_call(string fileId)   // C1
        {
            var result = await Node().RunAsync(Context(Params("get", fileId: fileId)));

            ShouldBeError(result, "File ID is required");
            _handler.Requests.Should().BeEmpty();
            _auth.Verify(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Rename_without_a_name_is_an_item_error_without_a_call()   // C2
        {
            var result = await Node().RunAsync(Context(Params("rename", newName: " ")));

            ShouldBeError(result, "New name is required");
            _handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Unknown_operation_fails_every_item_without_a_call()   // C3
        {
            var result = await Node().RunAsync(Context(Params("move"), "a", "b"));

            result.OutputItems.Should().HaveCount(2).And.OnlyContain(o => o.Data.Output["message"].AsString == "Unknown operation 'move'");
            _handler.Requests.Should().BeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a url")]
        [InlineData("ftp://blocksapi.dev.slsblx.com")]
        [InlineData("http://10.0.0.5")]
        public async Task An_unusable_or_private_base_fails_every_item_without_a_call(string? apiBaseUrl)   // C6
        {
            var result = await Node(apiBaseUrl).RunAsync(Context(Params("get")));

            ShouldBeError(result, "Storage API is not reachable");
            _handler.Requests.Should().BeEmpty();
            _auth.Verify(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_public_name_resolving_to_a_private_address_is_refused()   // C6
        {
            var result = await Node(guard: TestOutboundGuard.ResolvingTo(IPAddress.Parse("192.168.1.10"))).RunAsync(Context(Params("get")));

            ShouldBeError(result, "Storage API is not reachable");
            _handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task No_delegated_token_fails_the_item_and_sends_nothing()   // C4
        {
            _auth.Setup(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

            var result = await Node().RunAsync(Context(Params("get")));

            ShouldBeError(result, NoDelegatedTokenException.DefaultMessage);
            _handler.Requests.Should().BeEmpty();
        }

        [Theory]
        [InlineData("client-1", null)]
        [InlineData("", "cc-token")]
        public async Task A_missing_client_credential_token_fails_the_item_and_sends_nothing(string clientId, string? token)   // C5
        {
            _tokens.Setup(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(token);
            var parameters = Params("get", auth: "clientCredential");
            parameters["clientId"] = clientId;

            var result = await Node().RunAsync(Context(parameters));

            ShouldBeError(result, ActionStorageV2Node.MessageTokenFailed);
            _handler.Requests.Should().BeEmpty();
        }

        // ---------------------------------------------------------------- blocks-data answers

        [Fact]
        public async Task Get_denied_or_missing_says_not_found_or_no_access()   // C7, C13
        {
            _handler.Respond(HttpStatusCode.OK, """{"isSuccess":false,"errors":{"access":"forbidden"}}""");

            var result = await Node().RunAsync(Context(Params("get")));

            ShouldBeError(result, "File 'file-1' was not found or you don't have access to it");
        }

        [Theory]
        [InlineData("""{"isSuccess":false,"errors":{"file_not_found":"file_with_id_file-1_not_exist"}}""", "File 'file-1' was not found")]
        [InlineData("""{"isSuccess":false,"errors":{"access":"forbidden"}}""", "You don't have access to delete file 'file-1'")]
        [InlineData("""{"isSuccess":false,"errors":{"Configuration":"configuration_not_found"}}""", "Storage request failed (Configuration)")]
        public async Task Delete_refusals_map_to_their_messages(string body, string message)   // C8
        {
            _handler.Respond(HttpStatusCode.OK, body);

            ShouldBeError(await Node().RunAsync(Context(Params("delete"))), message);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, """{"message":"File not found."}""", "File not found.")]
        [InlineData(HttpStatusCode.Conflict, """{"message":"A file with that name already exists in the target directory."}""", "A file with that name already exists in the target directory.")]
        [InlineData(HttpStatusCode.BadRequest, """{"message":"A file name is required."}""", "A file name is required.")]
        public async Task Rename_client_errors_carry_blocks_data_s_message(HttpStatusCode status, string body, string message)   // C9
        {
            _handler.Respond(status, body);

            ShouldBeError(await Node().RunAsync(Context(Params("rename", newName: "x.pdf"))), message);
        }

        [Theory]
        [InlineData("get", "get-file", HttpStatusCode.Forbidden)]
        [InlineData("delete", "delete-file", HttpStatusCode.Unauthorized)]
        [InlineData("rename", "rename-file", HttpStatusCode.Forbidden)]
        public async Task A_guard_refusal_names_the_missing_permission(string operation, string endpoint, HttpStatusCode status)   // C9, C10
        {
            _handler.Respond(status, "");

            var result = await Node().RunAsync(Context(Params(operation, newName: "x.pdf")));

            ShouldBeError(result,
                $"Storage refused the request ({(int)status}): the credentials lack the 'blocks-data::file::{endpoint}' permission or access to file 'file-1'");
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "Storage request failed (500)")]
        [InlineData(HttpStatusCode.TooManyRequests, "Storage request failed (429)")]
        [InlineData(HttpStatusCode.NotFound, "Storage request failed (404)")]
        public async Task Other_statuses_fail_the_item_with_the_status(HttpStatusCode status, string message)   // C11
        {
            _handler.Respond(status, "<html>secret details</html>");

            ShouldBeError(await Node().RunAsync(Context(Params("get"))), message);
        }

        [Fact]
        public async Task A_network_error_fails_the_item()   // C11
        {
            _handler.Throw(new HttpRequestException("boom"));

            ShouldBeError(await Node().RunAsync(Context(Params("get"))), "Storage request failed (network)");
        }

        [Fact]
        public async Task A_timeout_fails_the_item()   // C11
        {
            _handler.Throw(new TaskCanceledException());

            ShouldBeError(await Node().RunAsync(Context(Params("get"))), "Storage request failed (timeout)");
        }

        [Fact]
        public async Task Failed_and_passing_items_are_both_kept_and_the_node_succeeds()   // C12
        {
            _handler.RespondBy(body => body.Contains("\"bad\"")
                ? (HttpStatusCode.OK, """{"isSuccess":false,"errors":{"file_not_found":"x"}}""")
                : (HttpStatusCode.OK, """{"isSuccess":true}"""));

            var result = await Node().RunAsync(Context(Params("delete"), "good", "bad", "good2"));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Select(o => o.IsError).Should().Equal(false, true, false);
        }

        [Fact]
        public async Task Messages_and_logs_never_carry_the_token_the_body_or_the_url()   // C14
        {
            _handler.Respond(HttpStatusCode.InternalServerError, $"{{\"url\":\"{SignedUrl}\",\"token\":\"delegated-token\"}}");
            var sink = new CollectingSink();
            using var factory = new SerilogLoggerFactory(
                new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(sink).CreateLogger(), dispose: true);
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1", TenantId = TenantId, WorkflowId = "wf-1", WorkflowName = "wf",
                WorkflowSnapshot = new WorkflowEntity { ItemId = "wf-1", Name = "wf", TenantId = TenantId },
                TriggerMetadata = new TriggerMetadata(), TraceId = "0af7651916cd43dd8448eb211c80319c",
            };
            var context = Context(Params("get"));
            context.Log = new WorkflowExecutionLogger(factory.CreateLogger<WorkflowExecutionLogger>()).For(execution).ForNode("storage", 1);

            var result = await Node().RunAsync(context);

            Single(result)["message"].AsString.Should().Be("Storage request failed (500)");
            var lines = sink.Events.Select(e => e.RenderMessage()).ToList();
            string.Join('\n', lines).Should().NotContain("SECRET").And.NotContain("delegated-token");
            lines.Should().Contain(l => l.Contains("[wf:node.storage.token]") && l.EndsWith("Access token obtained."))
                .And.Contain(l => l.Contains("[wf:node.storage.finished]") && l.EndsWith("Storage get finished: 0 succeeded, 1 failed."));
        }

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = [];

            public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        }

        [Theory]
        [InlineData("https://blocksapi.dev.slsblx.com/data/v4", "get-file", "https://blocksapi.dev.slsblx.com/data/v4/files/get-file")]
        [InlineData("https://blocksapi.dev.slsblx.com/data/v4/", "delete-file", "https://blocksapi.dev.slsblx.com/data/v4/files/delete-file")]
        public void Files_uri_appends_files_and_the_endpoint_to_the_base(string apiBase, string path, string expected)   // A2
        {
            ActionStorageV2Node.FilesUri(apiBase, path).Should().Be(new Uri(expected));
        }

        // ---------------------------------------------------------------- stub

        private sealed record Captured(HttpMethod Method, Uri Uri, string? Authorization, string? BlocksKey, string Body);

        private sealed class StubHandler : HttpMessageHandler
        {
            private Func<string, (HttpStatusCode, string)> _respond = _ => (HttpStatusCode.OK, "{}");
            private Exception? _throw;

            public List<Captured> Requests { get; } = [];

            public void Respond(HttpStatusCode status, string body) => _respond = _ => (status, body);

            public void RespondBy(Func<string, (HttpStatusCode, string)> respond) => _respond = respond;

            public void Throw(Exception ex) => _throw = ex;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add(new Captured(
                    request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
                    request.Headers.TryGetValues("x-blocks-key", out var key) ? key.Single() : null, body));
                if (_throw is not null) throw _throw;
                var (status, responseBody) = _respond(body);
                return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
            }
        }
    }
}
