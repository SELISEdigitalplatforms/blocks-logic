using System.Net;
using Blocks.Genesis;
using DomainService.Shared.Services;
using FluentAssertions;
using Moq;

namespace XUnitTest.Services
{
    /// <summary>
    /// Unit tests for <see cref="HttpHelperServices"/>. Every method here is a thin wrapper whose
    /// whole purpose is that a transport failure never reaches the caller as an exception: callers
    /// treat a null result as "the call did not work". These pin that contract on each overload,
    /// along with the arguments handed to the underlying client.
    /// </summary>
    public class HttpHelperServicesTests
    {
        public sealed class Dto
        {
            public string? Name { get; set; }
        }

        private readonly Mock<IHttpService> _httpService = new();
        private readonly Mock<IHttpClientFactory> _clientFactory = new();
        private readonly List<HttpRequestMessage> _sent = [];

        private HttpHelperServices CreateService() =>
            new(_httpService.Object, _clientFactory.Object);

        private sealed class StubHandler(
            HttpStatusCode code, string body, List<HttpRequestMessage> seen, bool fail = false)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                seen.Add(request);
                if (fail) throw new HttpRequestException("network down");
                return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
            }
        }

        private void SetupClient(HttpStatusCode code, string body, bool fail = false) =>
            _clientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                          .Returns(new HttpClient(new StubHandler(code, body, _sent, fail)));

        // ---- GET ----

        [Fact]
        public async Task MakeHttpGetRequest_HandsBackWhatTheClientReturned()
        {
            _httpService.Setup(s => s.Get<Dto>("https://api.example.com", It.IsAny<Dictionary<string, string>>()))
                        .ReturnsAsync((new Dto { Name = "ada" }, "raw-body"));

            var (data, raw) = await CreateService().MakeHttpGetRequest<Dto>("https://api.example.com");

            data!.Name.Should().Be("ada");
            raw.Should().Be("raw-body");
        }

        [Fact]
        public async Task MakeHttpGetRequest_PassesTheHeadersThrough()
        {
            var headers = new Dictionary<string, string> { ["X-Api-Key"] = "k" };
            _httpService.Setup(s => s.Get<Dto>(It.IsAny<string>(), headers))
                        .ReturnsAsync((new Dto(), string.Empty));

            await CreateService().MakeHttpGetRequest<Dto>("https://api.example.com", null, headers);

            _httpService.Verify(s => s.Get<Dto>("https://api.example.com", headers), Times.Once);
        }

        [Fact]
        public async Task MakeHttpGetRequest_ReportsAFailureRatherThanThrowing()
        {
            _httpService.Setup(s => s.Get<Dto>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                        .ThrowsAsync(new HttpRequestException("dns failure"));

            var (data, raw) = await CreateService().MakeHttpGetRequest<Dto>("https://api.example.com");

            data.Should().BeNull();
            raw.Should().Be("Operation Failed.");
        }

        // ---- POST ----

        [Fact]
        public async Task MakeHttpPostRequest_HandsBackWhatTheClientReturned()
        {
            _httpService.Setup(s => s.Post<Dto>(
                            It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(),
                            It.IsAny<Dictionary<string, string>>()))
                        .ReturnsAsync((new Dto { Name = "created" }, "raw"));

            var (data, raw) = await CreateService().MakeHttpPostRequest<Dto>(
                new { a = 1 }, "https://api.example.com");

            data!.Name.Should().Be("created");
            raw.Should().Be("raw");
        }

        [Fact]
        public async Task MakeHttpPostRequest_DefaultsToJson()
        {
            _httpService.Setup(s => s.Post<Dto>(
                            It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(),
                            It.IsAny<Dictionary<string, string>>()))
                        .ReturnsAsync((new Dto(), string.Empty));

            await CreateService().MakeHttpPostRequest<Dto>(new { a = 1 }, "https://api.example.com");

            _httpService.Verify(s => s.Post<Dto>(
                It.IsAny<object>(), "https://api.example.com", "application/json",
                It.IsAny<Dictionary<string, string>>()), Times.Once);
        }

        [Fact]
        public async Task MakeHttpPostRequest_HonoursAnExplicitContentType()
        {
            _httpService.Setup(s => s.Post<Dto>(
                            It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(),
                            It.IsAny<Dictionary<string, string>>()))
                        .ReturnsAsync((new Dto(), string.Empty));

            await CreateService().MakeHttpPostRequest<Dto>(
                new { a = 1 }, "https://api.example.com", null, null, "application/xml");

            _httpService.Verify(s => s.Post<Dto>(
                It.IsAny<object>(), It.IsAny<string>(), "application/xml",
                It.IsAny<Dictionary<string, string>>()), Times.Once);
        }

        [Fact]
        public async Task MakeHttpPostRequest_ReportsAFailureRatherThanThrowing()
        {
            _httpService.Setup(s => s.Post<Dto>(
                            It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(),
                            It.IsAny<Dictionary<string, string>>()))
                        .ThrowsAsync(new TimeoutException("upstream timed out"));

            var (data, raw) = await CreateService().MakeHttpPostRequest<Dto>(
                new { a = 1 }, "https://api.example.com");

            data.Should().BeNull();
            raw.Should().Be("Operation Failed.");
        }

        // ---- the named-client overload ----

        [Fact]
        public async Task MakeHttpRequest_SendsTheChosenVerbToTheChosenUrl()
        {
            SetupClient(HttpStatusCode.OK, "{\"Name\":\"ok\"}");

            var (data, _) = await CreateService().MakeHttpRequest<Dto>(
                "named", "https://api.example.com/x", HttpMethod.Post, new { a = 1 });

            _sent.Should().ContainSingle();
            _sent[0].Method.Should().Be(HttpMethod.Post);
            _sent[0].RequestUri!.ToString().Should().Be("https://api.example.com/x");
            data!.Name.Should().Be("ok");
        }

        [Fact]
        public async Task MakeHttpRequest_AppliesTheBearerToken()
        {
            SetupClient(HttpStatusCode.OK, "{\"Name\":\"ok\"}");

            await CreateService().MakeHttpRequest<Dto>(
                "named", "https://api.example.com/x", HttpMethod.Get, null, null, "token-1");

            _sent[0].Headers.Authorization!.Parameter.Should().Be("token-1");
        }

        [Fact]
        public async Task MakeHttpRequest_ReportsAFailureRatherThanThrowing()
        {
            SetupClient(HttpStatusCode.OK, string.Empty, fail: true);

            var (data, raw) = await CreateService().MakeHttpRequest<Dto>(
                "named", "https://api.example.com/x", HttpMethod.Get);

            data.Should().BeNull();
            raw.Should().Be("Operation Failed.");
        }

        [Fact]
        public async Task MakeHttpRequest_UsesTheNamedClient()
        {
            SetupClient(HttpStatusCode.OK, "{\"Name\":\"ok\"}");

            await CreateService().MakeHttpRequest<Dto>(
                "geolocation", "https://api.example.com/x", HttpMethod.Get);

            _clientFactory.Verify(f => f.CreateClient("geolocation"), Times.Once);
        }
    }
}
