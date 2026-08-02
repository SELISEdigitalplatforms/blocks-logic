using System.Net;
using System.Text.Json;
using DomainService.MagicLink.Service;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MagicLinkEntity = DomainService.MagicLink.Models.MagicLink;

namespace XUnitTest.Links
{
    /// <summary>
    /// Unit tests for <see cref="MagicLinkActionExecutor"/>. An action link is a stored HTTP call
    /// that anyone holding the link can trigger, so the parts worth pinning are which verb is
    /// dispatched, what reaches the wire (url, query string, headers, body, bearer token), and that
    /// nothing thrown by the remote call escapes as an exception rather than a result.
    /// </summary>
    public class MagicLinkActionExecutorTests
    {
        private readonly List<HttpRequestMessage> _sent = [];
        private readonly List<string> _bodies = [];
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _responseBody = "{\"ok\":true}";
        private Exception? _transportFailure;

        private sealed class StubHandler(MagicLinkActionExecutorTests owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner._sent.Add(request);
                owner._bodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                if (owner._transportFailure is not null) throw owner._transportFailure;

                return new HttpResponseMessage(owner._status)
                {
                    Content = new StringContent(owner._responseBody),
                };
            }
        }

        private MagicLinkActionExecutor CreateExecutor()
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                   .Returns(() => new HttpClient(new StubHandler(this)));

            return new MagicLinkActionExecutor(
                new Mock<ILogger<MagicLinkActionExecutor>>().Object, factory.Object);
        }

        private static MagicLinkEntity Link(string? method = "GET", Action<MagicLinkEntity>? tweak = null)
        {
            var link = new MagicLinkEntity
            {
                ItemId = "link-1",
                ProjectKey = "tenant-1",
                Uri = "https://api.example.com/do",
                RequestMethod = method!,
            };
            tweak?.Invoke(link);
            return link;
        }

        // ---- dispatch ----

        [Theory]
        [InlineData("GET", "GET")]
        [InlineData("get", "GET")]
        [InlineData("Post", "POST")]
        [InlineData("PUT", "PUT")]
        [InlineData("delete", "DELETE")]
        public async Task ExecuteActionAsync_DispatchesTheStoredVerbCaseInsensitively(
            string stored, string expected)
        {
            await CreateExecutor().ExecuteActionAsync(Link(stored));

            _sent.Should().ContainSingle();
            _sent[0].Method.Method.Should().Be(expected);
        }

        [Fact]
        public async Task ExecuteActionAsync_RejectsALinkWithNoVerb()
        {
            var result = await CreateExecutor().ExecuteActionAsync(Link(""));

            result.IsSuccess.Should().BeFalse();
            result.StatusCode.Should().Be(400);
            result.ErrorMessage.Should().Be("RequestMethod is required for Action type links");
            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task ExecuteActionAsync_RejectsAVerbItDoesNotSupport()
        {
            var result = await CreateExecutor().ExecuteActionAsync(Link("PATCH"));

            result.IsSuccess.Should().BeFalse();
            result.StatusCode.Should().Be(400);
            result.ErrorMessage.Should().Be("Unsupported HTTP method: PATCH");
            _sent.Should().BeEmpty();
        }

        // ---- what reaches the wire ----

        [Fact]
        public async Task ExecuteActionAsync_CallsTheStoredUri()
        {
            await CreateExecutor().ExecuteActionAsync(Link());

            _sent[0].RequestUri!.ToString().Should().Be("https://api.example.com/do");
        }

        [Fact]
        public async Task ExecuteActionAsync_AppendsTheStoredQueryString()
        {
            await CreateExecutor().ExecuteActionAsync(
                Link(tweak: l => l.RequestEncodedQueryString = "a=1&b=2"));

            _sent[0].RequestUri!.ToString().Should().Be("https://api.example.com/do?a=1&b=2");
        }

        [Fact]
        public async Task ExecuteActionAsync_JoinsAQueryStringOntoAUriThatAlreadyHasOne()
        {
            await CreateExecutor().ExecuteActionAsync(Link(tweak: l =>
            {
                l.Uri = "https://api.example.com/do?existing=1";
                l.RequestEncodedQueryString = "a=1";
            }));

            _sent[0].RequestUri!.ToString().Should().Be("https://api.example.com/do?existing=1&a=1");
        }

        [Fact]
        public async Task ExecuteActionAsync_SendsTheBearerTokenWhenOneIsSupplied()
        {
            await CreateExecutor().ExecuteActionAsync(Link(), "token-1");

            _sent[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
            _sent[0].Headers.Authorization.Parameter.Should().Be("token-1");
        }

        [Fact]
        public async Task ExecuteActionAsync_SendsNoAuthorizationWhenThereIsNoToken()
        {
            await CreateExecutor().ExecuteActionAsync(Link());

            _sent[0].Headers.Authorization.Should().BeNull();
        }

        [Fact]
        public async Task ExecuteActionAsync_AddsTheStoredHeaders()
        {
            await CreateExecutor().ExecuteActionAsync(Link(tweak: l =>
                l.RequestHeaders = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["X-Trace"] = "abc",
                    ["X-Tenant"] = "tenant-1",
                })));

            _sent[0].Headers.GetValues("X-Trace").Single().Should().Be("abc");
            _sent[0].Headers.GetValues("X-Tenant").Single().Should().Be("tenant-1");
        }

        [Fact]
        public async Task ExecuteActionAsync_IgnoresHeadersWithNoName()
        {
            await CreateExecutor().ExecuteActionAsync(Link(tweak: l =>
                l.RequestHeaders = "{\"\":\"orphan\",\"X-Kept\":\"yes\"}"));

            _sent[0].Headers.GetValues("X-Kept").Single().Should().Be("yes");
        }

        [Fact]
        public async Task ExecuteActionAsync_SurvivesUnparseableStoredHeaders()
        {
            // A malformed header blob must not stop the call the link exists to make.
            var result = await CreateExecutor().ExecuteActionAsync(
                Link(tweak: l => l.RequestHeaders = "not json at all"));

            result.IsSuccess.Should().BeTrue();
            _sent.Should().ContainSingle();
        }

        [Fact]
        public async Task ExecuteActionAsync_SendsTheStoredPayloadOnAPost()
        {
            await CreateExecutor().ExecuteActionAsync(
                Link("POST", l => l.RequestPayload = "{\"name\":\"ada\"}"));

            _bodies[0].Should().Be("{\"name\":\"ada\"}");
        }

        [Fact]
        public async Task ExecuteActionAsync_SendsNoBodyWhenThePayloadIsEmpty()
        {
            await CreateExecutor().ExecuteActionAsync(Link("POST"));

            _bodies[0].Should().BeEmpty();
        }

        [Fact]
        public async Task ExecuteActionAsync_SendsTheStoredPayloadOnAPut()
        {
            await CreateExecutor().ExecuteActionAsync(
                Link("PUT", l => l.RequestPayload = "{\"name\":\"ada\"}"));

            _bodies[0].Should().Be("{\"name\":\"ada\"}");
        }

        // ---- results ----

        [Fact]
        public async Task ExecuteActionAsync_ReportsSuccessAndTheDecodedBody()
        {
            var result = await CreateExecutor().ExecuteActionAsync(Link());

            result.IsSuccess.Should().BeTrue();
            result.StatusCode.Should().Be(200);
            result.Data.Should().NotBeNull();
            result.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public async Task ExecuteActionAsync_ReportsAFailureStatusWithoutThrowing()
        {
            _status = HttpStatusCode.Forbidden;

            var result = await CreateExecutor().ExecuteActionAsync(Link());

            result.IsSuccess.Should().BeFalse();
            result.StatusCode.Should().Be(403);
            result.ErrorMessage.Should().Be("HTTP 403");
        }

        [Fact]
        public async Task ExecuteActionAsync_StillSucceedsWhenTheBodyIsNotJson()
        {
            _responseBody = "plain text, not json";

            var result = await CreateExecutor().ExecuteActionAsync(Link());

            // The status is what the caller acts on; an undecodable body is not a failure.
            result.IsSuccess.Should().BeTrue();
            result.Data.Should().BeNull();
        }

        [Fact]
        public async Task ExecuteActionAsync_HandlesAnEmptyResponseBody()
        {
            _responseBody = string.Empty;

            var result = await CreateExecutor().ExecuteActionAsync(Link());

            result.IsSuccess.Should().BeTrue();
            result.Data.Should().BeNull();
        }

        [Fact]
        public async Task ExecuteActionAsync_TurnsATransportFailureIntoAResultRatherThanAnException()
        {
            _transportFailure = new HttpRequestException("connection refused");

            var result = await CreateExecutor().ExecuteActionAsync(Link());

            result.IsSuccess.Should().BeFalse();
            result.StatusCode.Should().Be(500);
            result.ErrorMessage.Should().Contain("connection refused");
        }

        [Fact]
        public async Task ExecuteActionAsync_AsksForJson()
        {
            await CreateExecutor().ExecuteActionAsync(Link());

            _sent[0].Headers.Accept.Select(a => a.MediaType).Should().Contain("application/json");
        }
    }
}
