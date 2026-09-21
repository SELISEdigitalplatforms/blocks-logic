using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyTestService"/>: request validation (C8 — proxyId / draft / both / neither /
    /// invalid method / bad draft) and the happy paths that forward with <c>IsTest = true</c> so no
    /// execution row is written (H5).
    /// </summary>
    public class ProxyTestServiceTests
    {
        private const string Tenant = "T1";

        private readonly Mock<IProxyRepository> _proxyRepo = new();
        private readonly Mock<IProxyGatewayService> _gateway = new();
        private readonly ProxyTestService _service;

        public ProxyTestServiceTests()
        {
            _service = new ProxyTestService(
                _proxyRepo.Object, _gateway.Object, Mock.Of<ILogger<ProxyTestService>>());

            _gateway.Setup(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProxyForwardResult
                {
                    StatusCode = 200,
                    Outcome = ProxyExecutionOutcome.Success,
                    UpstreamStatusCode = 200,
                    UpstreamUrl = "https://api.weatherapi.com/v1/current.json?q=London&key=${SECRET.WEATHER_KEY}",
                    UpstreamHost = "api.weatherapi.com",
                    InjectedQueryKeys = new[] { "key" },
                    ResponseBody = "{\"ok\":1}",
                    ResponseContentType = "application/json",
                });
        }

        private static ProxyDetailEntity SavedProxy() => new()
        {
            ItemId = "proxy-1",
            TenantId = Tenant,
            Name = "Echo",
            Slug = "p-echo",
            Upstream = "https://localhost:5199/echo",
            Methods = new List<HttpMethodType> { HttpMethodType.Get, HttpMethodType.Post },
            Enabled = true,
        };

        [Fact]
        public async Task Test_BothProxyIdAndDraft_Returns400_NoForward()
        {
            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto
            {
                ProxyId = "proxy-1",
                Draft = new ProxyTestDraftDto { Upstream = "https://x.test", Methods = new() { "GET" } },
                Method = "GET",
            });

            result.IsSuccess.Should().BeFalse();
            result.HttpStatus.Should().Be(400);
            result.Code.Should().Be("PROXY_VALIDATION");
            _gateway.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Test_NeitherProxyIdNorDraft_Returns400_NoForward()
        {
            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto { Method = "GET" });

            result.IsSuccess.Should().BeFalse();
            result.HttpStatus.Should().Be(400);
            _gateway.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Test_MissingMethod_Returns400()
        {
            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto { ProxyId = "proxy-1" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("method");
            _gateway.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Test_ProxyIdNotFound_Returns400_WithProxyIdError()
        {
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "missing")).ReturnsAsync((ProxyDetailEntity?)null);

            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto { ProxyId = "missing", Method = "GET" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("proxyId");
            _gateway.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Test_MethodNotEnabledForSavedProxy_Returns400_WithFieldError()
        {
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "proxy-1")).ReturnsAsync(SavedProxy());

            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto { ProxyId = "proxy-1", Method = "PUT" });

            result.IsSuccess.Should().BeFalse();
            result.HttpStatus.Should().Be(400);
            result.Errors!["method"].Should().Be("PUT is not enabled for this proxy.");
            _gateway.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Test_DraftFailsPhase1Validation_Returns400()
        {
            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto
            {
                Draft = new ProxyTestDraftDto { Upstream = "http://not-https.test", Methods = new() { "GET" } },
                Method = "GET",
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("upstream");
            _gateway.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Test_ValidSavedProxy_Forwards_AsTest_AndReturnsResult()
        {
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "proxy-1")).ReturnsAsync(SavedProxy());
            ProxyForwardRequest? forwarded = null;
            _gateway.Setup(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ProxyForwardRequest, CancellationToken>((r, _) => forwarded = r)
                .ReturnsAsync(new ProxyForwardResult { StatusCode = 200, Outcome = ProxyExecutionOutcome.Success });

            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto
            {
                ProxyId = "proxy-1",
                Method = "get",
                PathSuffix = "ping",
                Query = "z=9",
            });

            result.IsSuccess.Should().BeTrue();
            result.Result!.Status.Should().Be(200);
            forwarded!.IsTest.Should().BeTrue();
            forwarded.Method.Should().Be("GET");
            forwarded.ResolvedConfig!.ProxyId.Should().Be("proxy-1");
            forwarded.PathSuffix.Should().Be("ping");
            forwarded.IncomingQuery.Should().Be("z=9");
        }

        [Fact]
        public async Task Test_ValidDraft_WithMethodOverride_PassesItToTheResolvedConfig()
        {
            ProxyForwardRequest? forwarded = null;
            _gateway.Setup(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ProxyForwardRequest, CancellationToken>((r, _) => forwarded = r)
                .ReturnsAsync(new ProxyForwardResult { StatusCode = 200, Outcome = ProxyExecutionOutcome.Success });

            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto
            {
                Draft = new ProxyTestDraftDto
                {
                    Upstream = "https://api.x.test",
                    Methods = new() { "GET", "POST" },
                    MethodConfigs = new()
                    {
                        new ProxyMethodConfigInputDto { Method = "POST", Upstream = "https://api.x.test/v2" },
                    },
                },
                Method = "POST",
            });

            result.IsSuccess.Should().BeTrue();
            forwarded!.ResolvedConfig!.MethodConfigs.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new { Method = HttpMethodType.Post, Upstream = "https://api.x.test/v2" });
        }

        [Fact]
        public async Task Test_DraftMethodOverrideForNonDraftMethod_Returns400()
        {
            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto
            {
                Draft = new ProxyTestDraftDto
                {
                    Upstream = "https://api.x.test",
                    Methods = new() { "GET" },
                    MethodConfigs = new()
                    {
                        new ProxyMethodConfigInputDto { Method = "POST", Upstream = "https://api.x.test/v2" },
                    },
                },
                Method = "GET",
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("methodConfigs");
            _gateway.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Test_ValidDraft_Forwards_AsTest_WithoutTouchingTheRepository()
        {
            var result = await _service.TestAsync(Tenant, "u1", new ProxyTestRequestDto
            {
                Draft = new ProxyTestDraftDto
                {
                    Upstream = "https://api.weatherapi.com/v1/current.json",
                    Methods = new() { "GET" },
                    Query = new() { new ProxyKeyValueInputDto { Key = "key", Value = "${SECRET.WEATHER_KEY}" } },
                },
                Method = "GET",
                Query = "q=London",
            });

            result.IsSuccess.Should().BeTrue();
            result.Result!.Ok.Should().BeTrue();
            result.Result.InjectedQueryKeys.Should().Contain("key");
            _proxyRepo.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _gateway.Verify(g => g.ForwardAsync(It.Is<ProxyForwardRequest>(r => r.IsTest), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
