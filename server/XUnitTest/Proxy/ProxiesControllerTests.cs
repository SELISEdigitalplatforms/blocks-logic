using Blocks.Genesis;
using Common.InternalService.Access;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Services;
using Utilities.Api.Controllers;
using XUnitTest.TestHelpers;

namespace XUnitTest.Proxy
{
    public class ProxiesControllerTests : IDisposable
    {
        private readonly Mock<IProxyService> _proxyService = new();
        private readonly Mock<IProxyVersionService> _versionService = new();
        private readonly Mock<IProxyTestService> _testService = new();
        private readonly Mock<IProxyExecutionService> _executionService = new();
        private readonly Mock<IProxyGatewayService> _gatewayService = new();
        private readonly Mock<IEndpointAccessAuthorizer> _accessAuthorizer = new();
        private readonly ProxiesController _controller;

        public ProxiesControllerTests()
        {
            TestBlocksContext.Set("tenant-abc");
            _controller = new ProxiesController(
                _proxyService.Object, _versionService.Object, _testService.Object, _executionService.Object,
                _gatewayService.Object, _accessAuthorizer.Object, NullLogger<ProxiesController>.Instance);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static int StatusOf(IActionResult result) =>
            result switch
            {
                ObjectResult obj => obj.StatusCode ?? 200,
                StatusCodeResult code => code.StatusCode,
                _ => throw new InvalidOperationException($"Unexpected result type {result.GetType().Name}"),
            };

        [Fact]
        public async Task List_ReturnsOk_WithServiceResult()
        {
            var expected = new ProxyGetAllResponseDto();
            _proxyService.Setup(s => s.GetAllAsync("tenant-abc", It.IsAny<ProxyGetAllRequestDto>())).ReturnsAsync(expected);

            var result = await _controller.List(new ProxyGetAllRequestDto());

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task List_MissingTenantContext_Returns401_InsteadOfEmptyList()
        {
            TestBlocksContext.Clear();

            var result = await _controller.List(new ProxyGetAllRequestDto());

            StatusOf(result).Should().Be(401);
            _proxyService.Verify(
                s => s.GetAllAsync(It.IsAny<string>(), It.IsAny<ProxyGetAllRequestDto>()),
                Times.Never);
        }

        [Fact]
        public async Task Get_ReturnsOk_WithServiceResult()
        {
            var expected = new ProxyGetResponseDto();
            _proxyService.Setup(s => s.GetAsync("tenant-abc", It.IsAny<ProxyGetRequestDto>())).ReturnsAsync(expected);

            var result = await _controller.Get("p1");

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task Create_Returns201_WhenServiceSucceeds()
        {
            _proxyService.Setup(s => s.CreateAsync("tenant-abc", It.IsAny<ProxyCreateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1", 201));

            var result = await _controller.Create(new ProxyCreateRequestDto());

            StatusOf(result).Should().Be(201);
        }

        [Fact]
        public async Task Create_Returns400_OnValidationFailure()
        {
            _proxyService.Setup(s => s.CreateAsync("tenant-abc", It.IsAny<ProxyCreateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(400, "PROXY_VALIDATION", "bad"));

            var result = await _controller.Create(new ProxyCreateRequestDto());

            StatusOf(result).Should().Be(400);
        }

        [Fact]
        public async Task Create_Returns409_OnSlugConflict()
        {
            _proxyService.Setup(s => s.CreateAsync("tenant-abc", It.IsAny<ProxyCreateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(409, "PROXY_SLUG_CONFLICT", "exists"));

            var result = await _controller.Create(new ProxyCreateRequestDto());

            StatusOf(result).Should().Be(409);
        }

        [Fact]
        public async Task Update_Returns200_WhenServiceSucceeds()
        {
            _proxyService.Setup(s => s.UpdateAsync("tenant-abc", It.IsAny<ProxyUpdateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1"));

            var result = await _controller.Update("p1", new ProxyUpdateRequestDto());

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task Update_Returns404_WhenServiceReportsNotFound()
        {
            _proxyService.Setup(s => s.UpdateAsync("tenant-abc", It.IsAny<ProxyUpdateRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(404, "PROXY_NOT_FOUND", "missing"));

            var result = await _controller.Update("p1", new ProxyUpdateRequestDto());

            StatusOf(result).Should().Be(404);
        }

        [Fact]
        public async Task SetEnabled_Returns200_WhenServiceSucceeds()
        {
            _proxyService.Setup(s => s.ToggleAsync("tenant-abc", It.IsAny<ProxyToggleRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1"));

            var result = await _controller.SetEnabled("p1", new ProxyToggleRequestDto { Enabled = false });

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task Delete_Returns200_WhenServiceSucceeds()
        {
            _proxyService.Setup(s => s.DeleteAsync("tenant-abc", It.IsAny<ProxyDeleteRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1"));

            var result = await _controller.Delete("p1");

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task Delete_Returns404_WhenServiceReportsNotFound()
        {
            _proxyService.Setup(s => s.DeleteAsync("tenant-abc", It.IsAny<ProxyDeleteRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(404, "PROXY_NOT_FOUND", "missing"));

            var result = await _controller.Delete("p1");

            StatusOf(result).Should().Be(404);
        }

        [Fact]
        public async Task ListVersions_Returns200_WhenServiceSucceeds()
        {
            _versionService.Setup(s => s.GetVersionsAsync("tenant-abc", It.IsAny<ProxyGetVersionsRequestDto>()))
                .ReturnsAsync(new ProxyGetVersionsResponseDto());

            var result = await _controller.ListVersions("p1", new ProxyGetVersionsRequestDto());

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task ListVersions_Returns404_WhenServiceReportsUnknownProxy()
        {
            _versionService.Setup(s => s.GetVersionsAsync("tenant-abc", It.IsAny<ProxyGetVersionsRequestDto>()))
                .ReturnsAsync(new ProxyGetVersionsResponseDto { HttpStatus = 404, Code = "PROXY_NOT_FOUND" });

            var result = await _controller.ListVersions("p1", new ProxyGetVersionsRequestDto());

            StatusOf(result).Should().Be(404);
        }

        [Fact]
        public async Task Revert_Returns200_WhenServiceSucceeds()
        {
            _versionService.Setup(s => s.RevertAsync("tenant-abc", It.IsAny<ProxyRevertRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Success("p1"));

            var result = await _controller.Revert("p1", "v1");

            StatusOf(result).Should().Be(200);
        }

        [Fact]
        public async Task Revert_Returns409_WhenProxyDeleted()
        {
            _versionService.Setup(s => s.RevertAsync("tenant-abc", It.IsAny<ProxyRevertRequestDto>()))
                .ReturnsAsync(ProxyMutationResponse.Failure(409, "PROXY_DELETED", "gone"));

            var result = await _controller.Revert("p1", "v1");

            StatusOf(result).Should().Be(409);
        }

        [Fact]
        public async Task Test_Returns200_WithServiceResult_WhenSuccessful()
        {
            var payload = new ProxyTestResponseDto { Ok = true, Status = 200, Outcome = "Success" };
            _testService.Setup(s => s.TestAsync("tenant-abc", It.IsAny<string?>(), It.IsAny<ProxyTestRequestDto>()))
                .ReturnsAsync(ProxyTestOutcome.Ok(payload));

            var result = await _controller.Test(new ProxyTestRequestDto { ProxyId = "p1", Method = "GET" });

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(payload);
        }

        [Fact]
        public async Task Test_Returns400_WithCodeAndErrors_OnValidationFailure()
        {
            _testService.Setup(s => s.TestAsync("tenant-abc", It.IsAny<string?>(), It.IsAny<ProxyTestRequestDto>()))
                .ReturnsAsync(ProxyTestOutcome.Validation(new Dictionary<string, string> { ["method"] = "PUT is not enabled for this proxy." }));

            var result = await _controller.Test(new ProxyTestRequestDto { ProxyId = "p1", Method = "PUT" });

            StatusOf(result).Should().Be(400);
        }

        // ---------- Phase 3 : GetExecutions / GetExecution / GetOverview ----------

        [Fact]
        public async Task ListExecutions_ReturnsServiceHttpStatus()
        {
            _executionService.Setup(s => s.GetExecutionsAsync("tenant-abc", It.IsAny<ProxyGetExecutionsRequestDto>()))
                .ReturnsAsync(new ProxyGetExecutionsResponseDto { HttpStatus = 404, Code = "PROXY_NOT_FOUND" });

            var result = await _controller.ListExecutions("ghost", new ProxyGetExecutionsRequestDto());

            StatusOf(result).Should().Be(404);
        }

        [Fact]
        public async Task GetExecution_Returns200_WithNullData_OnMismatch()
        {
            _executionService.Setup(s => s.GetExecutionAsync("tenant-abc", It.IsAny<ProxyGetExecutionRequestDto>()))
                .ReturnsAsync(new ProxyGetExecutionResponseDto { Data = null });

            var result = await _controller.GetExecution("p1", "e1");

            StatusOf(result).Should().Be(200);
            result.Should().BeOfType<ObjectResult>()
                .Which.Value.Should().BeOfType<ProxyGetExecutionResponseDto>()
                .Which.Data.Should().BeNull();
        }

        [Fact]
        public async Task GetOverview_ReturnsServiceHttpStatus()
        {
            _executionService.Setup(s => s.GetOverviewAsync("tenant-abc", It.IsAny<ProxyGetOverviewRequestDto>()))
                .ReturnsAsync(new ProxyGetOverviewResponseDto { Data = new ProxyOverviewDto() });

            var result = await _controller.GetOverview("p1");

            StatusOf(result).Should().Be(200);
        }

        // ---------- The URL identifies the resource: a route id always beats a payload id ----------

        [Fact]
        public async Task Update_TakesTheProxyIdFromTheRoute_NotTheBody()
        {
            ProxyUpdateRequestDto? seen = null;
            _proxyService.Setup(s => s.UpdateAsync("tenant-abc", It.IsAny<ProxyUpdateRequestDto>()))
                .Callback<string, ProxyUpdateRequestDto>((_, dto) => seen = dto)
                .ReturnsAsync(ProxyMutationResponse.Success("from-route"));

            await _controller.Update("from-route", new ProxyUpdateRequestDto { ItemId = "from-body" });

            seen!.ItemId.Should().Be("from-route");
        }

        [Fact]
        public async Task SetEnabled_TakesTheProxyIdFromTheRoute_NotTheBody()
        {
            ProxyToggleRequestDto? seen = null;
            _proxyService.Setup(s => s.ToggleAsync("tenant-abc", It.IsAny<ProxyToggleRequestDto>()))
                .Callback<string, ProxyToggleRequestDto>((_, dto) => seen = dto)
                .ReturnsAsync(ProxyMutationResponse.Success("from-route"));

            await _controller.SetEnabled("from-route", new ProxyToggleRequestDto { ItemId = "from-body", Enabled = false });

            seen!.ItemId.Should().Be("from-route");
            seen.Enabled.Should().BeFalse();
        }

        [Fact]
        public async Task ListExecutions_TakesTheProxyIdFromTheRoute_NotTheQueryString()
        {
            ProxyGetExecutionsRequestDto? seen = null;
            _executionService.Setup(s => s.GetExecutionsAsync("tenant-abc", It.IsAny<ProxyGetExecutionsRequestDto>()))
                .Callback<string, ProxyGetExecutionsRequestDto>((_, dto) => seen = dto)
                .ReturnsAsync(new ProxyGetExecutionsResponseDto());

            await _controller.ListExecutions(
                "from-route",
                new ProxyGetExecutionsRequestDto { ProxyId = "from-query", StatusClass = "4xx" });

            seen!.ProxyId.Should().Be("from-route");
            seen.StatusClass.Should().Be("4xx");
        }

        [Fact]
        public async Task ListVersions_TakesTheProxyIdFromTheRoute_NotTheQueryString()
        {
            ProxyGetVersionsRequestDto? seen = null;
            _versionService.Setup(s => s.GetVersionsAsync("tenant-abc", It.IsAny<ProxyGetVersionsRequestDto>()))
                .Callback<string, ProxyGetVersionsRequestDto>((_, dto) => seen = dto)
                .ReturnsAsync(new ProxyGetVersionsResponseDto());

            await _controller.ListVersions("from-route", new ProxyGetVersionsRequestDto { ProxyId = "from-query" });

            seen!.ProxyId.Should().Be("from-route");
        }

        // ---------- Gateway: "Who can call it" ----------

        private void GivenGatewayRequest(string method = "GET", string? tenantHeader = "tenant-abc")
        {
            var http = new DefaultHttpContext();
            http.Request.Method = method;
            http.Request.Path = "/api/proxy/gateway/stripe/charges";
            http.Request.Body = new MemoryStream();
            if (tenantHeader is not null)
            {
                http.Request.Headers["x-blocks-key"] = tenantHeader;
            }

            _controller.ControllerContext = new ControllerContext { HttpContext = http };
            _accessAuthorizer.Setup(a => a.ResolveTenantIdAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(tenantHeader);
        }

        private static ProxyResolvedConfig ResolvedWith(EndpointAccessPolicy access) => new()
        {
            ProxyId = "p1",
            Slug = "stripe",
            Upstream = "https://api.stripe.com",
            Methods = [HttpMethodType.Get],
            Access = access,
        };

        private static ProxyForwardResult Ok() => new()
        {
            StatusCode = 200,
            Outcome = ProxyExecutionOutcome.Success,
            ResponseBytes = [],
        };

        [Fact]
        public async Task Gateway_NoTenant_Returns401_WithoutTouchingTheForwarder()
        {
            GivenGatewayRequest(tenantHeader: null);

            var result = await _controller.Gateway("stripe", "charges");

            StatusOf(result).Should().Be(401);
            _gatewayService.Verify(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Gateway_UnknownSlug_IsCheckedAgainstTheTokenPolicy_So401ComesBefore404()
        {
            GivenGatewayRequest();
            _gatewayService.Setup(g => g.ResolveAsync("tenant-abc", "stripe", It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProxyResolvedConfig?)null);
            EndpointAccessPolicy? seenPolicy = null;
            _accessAuthorizer.Setup(a => a.AuthorizeAsync(It.IsAny<HttpRequest>(), "tenant-abc", It.IsAny<EndpointAccessPolicy>(), It.IsAny<CancellationToken>()))
                .Callback<HttpRequest, string, EndpointAccessPolicy, CancellationToken>((_, _, p, _) => seenPolicy = p)
                .ReturnsAsync(EndpointAccessDecision.Unauthenticated("no token"));

            var result = await _controller.Gateway("stripe", "charges");

            StatusOf(result).Should().Be(401);
            seenPolicy!.Kind.Should().Be(EndpointAccessKind.BlocksToken);
            _gatewayService.Verify(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Gateway_PublicPolicy_ForwardsWithNoIdentity()
        {
            GivenGatewayRequest();
            var config = ResolvedWith(EndpointAccessPolicy.AllowPublic());
            _gatewayService.Setup(g => g.ResolveAsync("tenant-abc", "stripe", It.IsAny<CancellationToken>())).ReturnsAsync(config);
            _accessAuthorizer.Setup(a => a.AuthorizeAsync(It.IsAny<HttpRequest>(), "tenant-abc", config.Access, It.IsAny<CancellationToken>()))
                .ReturnsAsync(EndpointAccessDecision.Public());
            ProxyForwardRequest? seen = null;
            _gatewayService.Setup(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ProxyForwardRequest, CancellationToken>((r, _) => seen = r)
                .ReturnsAsync(Ok());

            await _controller.Gateway("stripe", "charges");

            seen!.TenantId.Should().Be("tenant-abc");
            seen.UserId.Should().BeNull();
            seen.ResolvedConfig.Should().BeSameAs(config);
            seen.ForbiddenReason.Should().BeNull();
            seen.CallerKind.Should().Be(ProxyCallerKind.Client);
        }

        [Fact]
        public async Task Gateway_ForbiddenCaller_IsHandedToTheForwarderAsA403Row()
        {
            GivenGatewayRequest();
            var config = ResolvedWith(EndpointAccessPolicy.RequireToken());
            _gatewayService.Setup(g => g.ResolveAsync("tenant-abc", "stripe", It.IsAny<CancellationToken>())).ReturnsAsync(config);
            _accessAuthorizer.Setup(a => a.AuthorizeAsync(It.IsAny<HttpRequest>(), "tenant-abc", config.Access, It.IsAny<CancellationToken>()))
                .ReturnsAsync(EndpointAccessDecision.Forbidden("missing role"));
            ProxyForwardRequest? seen = null;
            _gatewayService.Setup(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ProxyForwardRequest, CancellationToken>((r, _) => seen = r)
                .ReturnsAsync(new ProxyForwardResult
                {
                    StatusCode = 403, Outcome = ProxyExecutionOutcome.Forbidden, ErrorMessage = "missing role",
                });

            var result = await _controller.Gateway("stripe", "charges");

            StatusOf(result).Should().Be(403);
            seen!.ForbiddenReason.Should().Be("missing role");
        }

        [Fact]
        public async Task Gateway_AllowedTokenCaller_CarriesTheIdentityOntoTheForward()
        {
            GivenGatewayRequest();
            var config = ResolvedWith(EndpointAccessPolicy.RequireToken());
            _gatewayService.Setup(g => g.ResolveAsync("tenant-abc", "stripe", It.IsAny<CancellationToken>())).ReturnsAsync(config);
            var context = BlocksContext.Create(
                tenantId: "tenant-abc", roles: ["admin"], userId: "user-9", isAuthenticated: true, requestUri: "/",
                organizationId: "", expireOn: DateTime.UtcNow.AddHours(1), email: "", permissions: [], userName: "jane",
                phoneNumber: "", displayName: "Jane", oauthToken: "", originalTenantId: "tenant-abc", applicationDomain: "",
                impersonated: false, impersonationSessionId: "");
            _accessAuthorizer.Setup(a => a.AuthorizeAsync(It.IsAny<HttpRequest>(), "tenant-abc", config.Access, It.IsAny<CancellationToken>()))
                .ReturnsAsync(EndpointAccessDecision.Allowed(context, new System.Security.Claims.ClaimsPrincipal(), "tok"));
            ProxyForwardRequest? seen = null;
            _gatewayService.Setup(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ProxyForwardRequest, CancellationToken>((r, _) => seen = r)
                .ReturnsAsync(Ok());

            await _controller.Gateway("stripe", "charges");

            seen!.UserId.Should().Be("user-9");
            seen.UserName.Should().Be("jane");
            seen.CallerImpersonated.Should().BeFalse();
            seen.CallerImpersonationSessionId.Should().BeNull();
            seen.ForbiddenReason.Should().BeNull();
        }

        [Fact]
        public async Task Gateway_ImpersonatedCaller_IsEvaluatedLikeAnyToken_AndFlaggedOnTheForward()
        {
            GivenGatewayRequest();
            var config = ResolvedWith(EndpointAccessPolicy.RequireToken());
            _gatewayService.Setup(g => g.ResolveAsync("tenant-abc", "stripe", It.IsAny<CancellationToken>())).ReturnsAsync(config);
            var context = BlocksContext.Create(
                tenantId: "tenant-abc", roles: ["admin"], userId: "cloud-user", isAuthenticated: true, requestUri: "/",
                organizationId: "", expireOn: DateTime.UtcNow.AddHours(1), email: "", permissions: [], userName: "root-admin",
                phoneNumber: "", displayName: "Root Admin", oauthToken: "", originalTenantId: "root-tenant", applicationDomain: "",
                impersonated: true, impersonationSessionId: "imp-42");
            _accessAuthorizer.Setup(a => a.AuthorizeAsync(It.IsAny<HttpRequest>(), "tenant-abc", config.Access, It.IsAny<CancellationToken>()))
                .ReturnsAsync(EndpointAccessDecision.Allowed(context, new System.Security.Claims.ClaimsPrincipal(), "tok"));
            ProxyForwardRequest? seen = null;
            _gatewayService.Setup(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ProxyForwardRequest, CancellationToken>((r, _) => seen = r)
                .ReturnsAsync(Ok());

            await _controller.Gateway("stripe", "charges");

            seen!.TenantId.Should().Be("tenant-abc");
            seen.UserId.Should().Be("cloud-user");
            seen.CallerImpersonated.Should().BeTrue();
            seen.CallerImpersonationSessionId.Should().Be("imp-42");
        }
    }
}
