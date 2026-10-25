using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionHttpRequestV1;
using Workflow.DomainService.Services;
using Blocks.Genesis.Entities.Logic;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    public class WorkflowOutboundGuardTests
    {
        private const string BlocksUrl = "https://api.seliseblocks.com/x";
        private const string ForeignUrl = "https://evil-seliseblocks.com/x";

        private static readonly IPAddress PrivateAddress = IPAddress.Parse("10.1.2.3");

        // ---------- address rule ----------

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("127.10.0.1")]
        [InlineData("0.0.0.0")]
        [InlineData("10.0.0.1")]
        [InlineData("172.16.0.1")]
        [InlineData("172.31.255.255")]
        [InlineData("192.168.1.1")]
        [InlineData("169.254.169.254")]
        [InlineData("100.64.0.1")]
        [InlineData("224.0.0.1")]
        [InlineData("255.255.255.255")]
        [InlineData("::")]
        [InlineData("::1")]
        [InlineData("fe80::1")]
        [InlineData("fc00::1")]
        [InlineData("fd12:3456::1")]
        [InlineData("ff02::1")]
        [InlineData("::ffff:127.0.0.1")]
        [InlineData("::ffff:169.254.169.254")]
        [InlineData("::ffff:10.0.0.1")]
        [InlineData("64:ff9b::a9fe:a9fe")]
        [InlineData("2002:7f00:1::1")]
        public void IsBlockedAddress_blocks_internal_ranges(string address)
        {
            WorkflowOutboundGuard.IsBlockedAddress(IPAddress.Parse(address)).Should().BeTrue();
        }

        [Theory]
        [InlineData("93.184.216.34")]
        [InlineData("8.8.8.8")]
        [InlineData("172.32.0.1")]
        [InlineData("100.128.0.1")]
        [InlineData("2606:4700::1111")]
        [InlineData("::ffff:8.8.8.8")]
        public void IsBlockedAddress_allows_public_addresses(string address)
        {
            WorkflowOutboundGuard.IsBlockedAddress(IPAddress.Parse(address)).Should().BeFalse();
        }

        [Fact]
        public void IsBlockedAddress_fails_closed_on_null()
        {
            WorkflowOutboundGuard.IsBlockedAddress(null).Should().BeTrue();
        }

        // ---------- pre-send check ----------

        [Theory]
        [InlineData("http://127.0.0.1/x")]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        [InlineData("http://2130706433/")]
        [InlineData("http://0x7f000001/")]
        [InlineData("http://[::1]/")]
        [InlineData("http://[::ffff:169.254.169.254]/")]
        [InlineData("http://10.0.0.1:8080/")]
        [InlineData("ftp://example.com/file")]
        [InlineData("file:///etc/hosts")]
        public async Task EnsureAllowedAsync_refuses_blocked_literals_and_schemes(string url)
        {
            var guard = TestOutboundGuard.ResolvingTo();

            var act = () => guard.EnsureAllowedAsync(new Uri(url));

            await act.Should().ThrowAsync<WorkflowOutboundBlockedException>();
        }

        [Fact]
        public async Task EnsureAllowedAsync_refuses_a_name_with_any_private_address()
        {
            var guard = TestOutboundGuard.ResolvingTo(TestOutboundGuard.PublicAddress, PrivateAddress);

            var act = () => guard.EnsureAllowedAsync(new Uri("https://intranet.example.com/"));

            await act.Should().ThrowAsync<WorkflowOutboundBlockedException>();
        }

        [Fact]
        public async Task EnsureAllowedAsync_allows_a_public_name()
        {
            var guard = TestOutboundGuard.ResolvingTo();

            var act = () => guard.EnsureAllowedAsync(new Uri("https://example.com/"));

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task EnsureAllowedAsync_allows_a_trusted_host_that_resolves_to_a_private_address()
        {
            var guard = TestOutboundGuard.ResolvingTo(PrivateAddress);

            var act = () => guard.EnsureAllowedAsync(new Uri(BlocksUrl));

            await act.Should().NotThrowAsync("in-cluster DNS may answer with a private address");
        }

        [Fact]
        public async Task EnsureAllowedAsync_does_not_trust_a_look_alike_host_that_resolves_to_a_private_address()
        {
            var guard = TestOutboundGuard.ResolvingTo(PrivateAddress);

            var act = () => guard.EnsureAllowedAsync(new Uri(ForeignUrl));

            await act.Should().ThrowAsync<WorkflowOutboundBlockedException>();
        }

        // ---------- trusted hosts ----------

        [Theory]
        [InlineData("https://seliseblocks.com/", true)]
        [InlineData("https://api.seliseblocks.com/", true)]
        [InlineData("https://API.SeliseBlocks.com./", true)]
        [InlineData("https://a.b.blocksdevelopers.com/", true)]
        [InlineData("https://evil-seliseblocks.com/", false)]
        [InlineData("https://seliseblocks.com.evil.com/", false)]
        [InlineData("https://notblocksdevelopers.com/", false)]
        [InlineData("https://10.0.0.1/", false)]
        public void IsTrustedHost_matches_on_a_dot_boundary(string url, bool expected)
        {
            TestOutboundGuard.ResolvingTo().IsTrustedHost(new Uri(url)).Should().Be(expected);
        }

        [Fact]
        public void Configured_trusted_hosts_replace_the_defaults()
        {
            var options = Options.Create(new WorkflowHttpNodeOptions { TrustedHosts = ["Blocks.Internal."] });
            var guard = new WorkflowOutboundGuard(options);

            guard.IsTrustedHost(new Uri("https://svc.blocks.internal/")).Should().BeTrue();
            guard.IsTrustedHost(new Uri(BlocksUrl)).Should().BeFalse();
        }

        // ---------- connect-time check ----------

        [Fact]
        public async Task Connect_refuses_when_dns_answers_private_at_connect_time()
        {
            var calls = 0;
            var guard = new WorkflowOutboundGuard(null,
                (_, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1
                    ? new[] { TestOutboundGuard.PublicAddress }
                    : new[] { IPAddress.Loopback }),
                WorkflowOutboundGuard.IsBlockedAddress);
            var uri = new Uri("http://rebind.example.com/");
            await guard.EnsureAllowedAsync(uri);

            using var client = new HttpClient(WorkflowOutboundGuard.CreatePrimaryHandler(guard));
            var act = () => client.GetAsync(uri);

            (await act.Should().ThrowAsync<HttpRequestException>())
                .WithInnerException<WorkflowOutboundBlockedException>();
        }

        [Fact]
        public async Task Redirect_drops_authorization_on_another_host_and_stops_after_five_hops()
        {
            // Loopback server; the guard's address rule is relaxed for this test only.
            var port = FreePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            var seen = new List<(string Path, string? Auth)>();
            var server = ServeAsync(listener, port, seen);

            // HttpListener binds each prefix to the first address its host resolves to; where "localhost"
            // is ::1 first (GitHub runners), that is a separate socket from 127.0.0.1. Connect each host
            // to the address its prefix is bound to, or the request lands on the wrong socket and 404s.
            var localhost = Dns.GetHostAddresses("localhost")[0];
            var guard = new WorkflowOutboundGuard(null,
                (host, _) => Task.FromResult(new[] { host == "localhost" ? localhost : IPAddress.Loopback }), _ => false);
            using var client = new HttpClient(WorkflowOutboundGuard.CreatePrimaryHandler(guard));

            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/cross");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-1");
            using var crossHost = await client.SendAsync(request);

            using var loop = await client.GetAsync($"http://127.0.0.1:{port}/loop/0");
            listener.Stop();
            await server;

            crossHost.StatusCode.Should().Be(HttpStatusCode.OK);
            seen[0].Should().Be(("/cross", "Bearer secret-1"));
            seen[1].Should().Be(("/landed", (string?)null), "the Authorization header must not follow a redirect to another host");
            loop.StatusCode.Should().Be(HttpStatusCode.Redirect, "the client stops following after 5 hops");
            seen.Count(s => s.Path.StartsWith("/loop/", StringComparison.Ordinal)).Should().Be(WorkflowOutboundGuard.MaxRedirects + 1);
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static async Task ServeAsync(HttpListener listener, int port, List<(string Path, string? Auth)> seen)
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return; // listener stopped
                }

                var path = ctx.Request.Url!.AbsolutePath;
                seen.Add((path, ctx.Request.Headers["Authorization"]));
                if (path == "/cross")
                {
                    ctx.Response.Redirect($"http://localhost:{port}/landed");
                }
                else if (path.StartsWith("/loop/", StringComparison.Ordinal))
                {
                    var next = int.Parse(path["/loop/".Length..], System.Globalization.CultureInfo.InvariantCulture) + 1;
                    ctx.Response.Redirect($"http://127.0.0.1:{port}/loop/{next}");
                }
                else
                {
                    ctx.Response.StatusCode = 200;
                }
                ctx.Response.Close();
            }
        }

        // ---------- response cap ----------

        [Fact]
        public async Task Reader_refuses_a_body_over_the_cap_with_content_length()
        {
            using var content = new ByteArrayContent(new byte[11]);

            var act = () => WorkflowHttpResponseReader.ReadAsStringCappedAsync(content, 10);

            await act.Should().ThrowAsync<ResponseTooLargeException>();
        }

        [Fact]
        public async Task Reader_refuses_a_body_over_the_cap_without_content_length()
        {
            using var content = new StreamContent(new NonSeekableStream(new byte[11]));
            content.Headers.ContentLength.Should().BeNull();

            var act = () => WorkflowHttpResponseReader.ReadAsStringCappedAsync(content, 10);

            await act.Should().ThrowAsync<ResponseTooLargeException>();
        }

        [Fact]
        public async Task Reader_reads_a_body_at_the_cap_and_honours_the_charset()
        {
            using var content = new ByteArrayContent(Encoding.Latin1.GetBytes("\"café\""));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "iso-8859-1" };

            var text = await WorkflowHttpResponseReader.ReadAsStringCappedAsync(content, 7);

            text.Should().Be("\"café\"");
        }

        private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
        {
            public override bool CanSeek => false;
        }

        // ---------- HTTP Request node ----------

        private sealed class CaptureHandler(string body) : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
        }

        private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
        {
            public string? LastName { get; private set; }

            public HttpClient CreateClient(string name)
            {
                LastName = name;
                return new HttpClient(handler, disposeHandler: false);
            }
        }

        private static NodeExecutionContext Context(BsonDocument parameters) => new()
        {
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            Parameters = parameters,
            InputItems = new List<WorkflowItemExecutionEntity>
            {
                new()
                {
                    Id = "item-1", WorkflowExecutionId = "exec-1", TenantId = "tenant-1", NodeId = "t",
                    NodeExecutionId = "ne-1", NodeName = "Trigger", Branch = "source",
                    ParentItemIds = new List<string>(), AncestorMap = new Dictionary<string, string>(),
                    Data = new NodeOutputItemData { Output = new BsonDocument() },
                },
            },
            IterationCount = 1,
            WorkflowContext = new BsonDocument(),
        };

        private static BsonDocument Params(string url, string authenticationType = "", BsonDocument? headers = null) => new()
        {
            { "httpMethod", "GET" },
            { "url", url },
            { "authenticationType", authenticationType },
            { "clientId", "client-1" },
            { "clientSecret", "secret-1" },
            { "haveHeaders", headers != null },
            { "headers", headers ?? new BsonDocument() },
        };

        private sealed record NodeRun(NodeExecutionResult Result, CaptureHandler Handler, Factory Factory,
            Mock<IWorkflowAuthService> Auth, Mock<IClientCredentialTokenService> ClientCredential);

        private static async Task<NodeRun> Run(BsonDocument parameters, string responseBody = "{\"ok\":true}", IPAddress? resolvesTo = null)
        {
            var handler = new CaptureHandler(responseBody);
            var factory = new Factory(handler);
            var auth = new Mock<IWorkflowAuthService>();
            auth.Setup(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("delegated-1");
            var clientCredential = new Mock<IClientCredentialTokenService>();
            clientCredential.Setup(c => c.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("cc-1");
            var guard = resolvesTo is null ? TestOutboundGuard.ResolvingTo() : TestOutboundGuard.ResolvingTo(resolvesTo);
            var node = new ActionHttpRequestV1Node(factory, auth.Object, clientCredential.Object,
                NullLogger<ActionHttpRequestV1Node>.Instance, guard);
            var result = await node.RunAsync(Context(parameters));
            return new NodeRun(result, handler, factory, auth, clientCredential);
        }

        private static string ErrorMessage(NodeExecutionResult result)
        {
            var output = result.OutputItems.Should().ContainSingle().Subject.Data.Output.AsBsonDocument;
            output["error"].AsBoolean.Should().BeTrue();
            return output["message"].AsString;
        }

        [Theory]
        [InlineData("blocksAuthentication")]
        [InlineData("clientCredential")]
        public async Task Node_refuses_a_blocks_token_for_a_foreign_host_and_sends_nothing(string mode)
        {
            var run = await Run(Params(ForeignUrl, mode));

            ErrorMessage(run.Result).Should().Be(BlocksTokenHostNotTrustedException.DefaultMessage);
            run.Handler.Requests.Should().BeEmpty();
            run.Auth.Verify(a => a.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
            run.ClientCredential.Verify(c => c.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("blocksAuthentication", "Bearer delegated-1")]
        [InlineData("clientCredential", "Bearer cc-1")]
        public async Task Node_sends_a_blocks_token_to_a_trusted_host_even_on_a_private_address(string mode, string expected)
        {
            var run = await Run(Params(BlocksUrl, mode), resolvesTo: PrivateAddress);

            run.Handler.Requests.Should().ContainSingle().Which.Headers.Authorization!.ToString().Should().Be(expected);
            run.Factory.LastName.Should().Be(WorkflowOutboundGuard.HttpClientName);
        }

        [Fact]
        public async Task Node_keeps_a_manual_authorization_header_for_a_foreign_host()
        {
            var run = await Run(Params(ForeignUrl, "blocksAuthentication",
                new BsonDocument { { "Authorization", "Bearer manual" } }));

            run.Handler.Requests.Should().ContainSingle().Which.Headers.Authorization!.ToString().Should().Be("Bearer manual");
        }

        [Fact]
        public async Task Node_without_authentication_calls_a_public_url_as_before()
        {
            var run = await Run(Params("https://example.com/items"));

            run.Result.OutputItems.Should().ContainSingle().Which.Data.Output.AsBsonDocument.Contains("error").Should().BeFalse();
            run.Handler.Requests.Should().ContainSingle().Which.Headers.Authorization.Should().BeNull();
        }

        [Theory]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        [InlineData("http://[::ffff:127.0.0.1]:8080/")]
        public async Task Node_refuses_a_blocked_address_and_sends_nothing(string url)
        {
            var run = await Run(Params(url));

            ErrorMessage(run.Result).Should().Be(WorkflowOutboundBlockedException.DefaultMessage);
            run.Handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Node_refuses_a_name_that_resolves_to_a_private_address()
        {
            var run = await Run(Params("https://internal.example.com/"), resolvesTo: PrivateAddress);

            ErrorMessage(run.Result).Should().Be(WorkflowOutboundBlockedException.DefaultMessage);
            run.Handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Node_fails_the_item_when_the_response_is_over_the_cap()
        {
            var big = "\"" + new string('a', (int)WorkflowOutboundGuard.MaxResponseBytes) + "\"";

            var run = await Run(Params("https://example.com/big"), big);

            ErrorMessage(run.Result).Should().Be(ResponseTooLargeException.DefaultMessage);
        }
    }
}
