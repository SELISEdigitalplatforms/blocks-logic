using System.Text;
using BlocksTemplate.Api.Controllers;
using Common.InternalService.Access;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The public route as a client sees it: which status each refusal maps to, in the gateway's
    /// error shape, and what the controller hands the service for the handler's <c>input</c>.
    /// </summary>
    public class FunctionsControllerInvokeTests
    {
        private readonly Mock<IFunctionInvocationService> _invocation = new();
        private readonly Mock<IEndpointAccessAuthorizer> _access = new();
        private readonly FunctionsController _controller;

        public FunctionsControllerInvokeTests()
        {
            _controller = new FunctionsController(
                new Mock<IFunctionService>().Object,
                new Mock<IFunctionDeploymentService>().Object,
                _invocation.Object,
                new Mock<IFunctionRunService>().Object,
                new Mock<IFunctionBuildService>().Object,
                new Mock<IFunctionAuditService>().Object,
                _access.Object);
        }

        private void GivenRequest(
            string method = "POST", string? tenant = "tenant-abc", string? body = "{\"qty\":2}",
            string? contentType = "application/json", string query = "", Action<HttpRequest>? more = null)
        {
            var http = new DefaultHttpContext();
            http.Request.Method = method;
            http.Request.Path = "/api/fn/fn_1/orders/42";
            http.Request.QueryString = new QueryString(query);
            var bytes = body is null ? [] : Encoding.UTF8.GetBytes(body);
            http.Request.Body = new MemoryStream(bytes);
            http.Request.ContentLength = bytes.Length;
            if (contentType is not null) http.Request.ContentType = contentType;
            if (tenant is not null) http.Request.Headers["x-blocks-key"] = tenant;
            more?.Invoke(http.Request);

            _controller.ControllerContext = new ControllerContext { HttpContext = http };
            _access.Setup(a => a.ResolveTenantIdAsync(It.IsAny<HttpRequest>())).ReturnsAsync(tenant);
        }

        private InvokeFunctionRequestDto? _seen;

        private void ServiceAnswers(InvokeResultDto result)
        {
            _invocation.Setup(s => s.InvokeHttpAsync("tenant-abc", "fn_1", It.IsAny<InvokeFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, InvokeFunctionRequestDto, CancellationToken>((_, _, r, _) => _seen = r)
                .ReturnsAsync(result);
        }

        private void ServiceThrows(Exception ex)
        {
            _invocation.Setup(s => s.InvokeHttpAsync("tenant-abc", "fn_1", It.IsAny<InvokeFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);
        }

        private static (int Status, string? Code) Outcome(IActionResult result)
        {
            var obj = result.Should().BeAssignableTo<ObjectResult>().Subject;
            var code = obj.Value?.GetType().GetProperty("code")?.GetValue(obj.Value) as string;
            return (obj.StatusCode ?? 200, code);
        }

        // ------------------------------------------------------------ refusals ----

        [Fact]
        public async Task No_tenant_is_401_and_the_service_is_never_asked()
        {
            GivenRequest(tenant: null);

            var result = await _controller.Invoke("fn_1", "orders/42", wait: false);

            Outcome(result).Should().Be((401, "FUNCTION_INVOKE_UNAUTHORIZED"));
            _invocation.Verify(s => s.InvokeHttpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<InvokeFunctionRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(typeof(FunctionAuthorizationException), 401, "FUNCTION_INVOKE_UNAUTHORIZED")]
        [InlineData(typeof(FunctionForbiddenException), 403, "FUNCTION_INVOKE_FORBIDDEN")]
        [InlineData(typeof(FunctionNotFoundException), 404, "FUNCTION_INVOKE_NOT_FOUND")]
        [InlineData(typeof(FunctionRequestTooLargeException), 413, "FUNCTION_INVOKE_REQUEST_TOO_LARGE")]
        [InlineData(typeof(FunctionValidationException), 400, "FUNCTION_INVOKE_INVALID_REQUEST")]
        [InlineData(typeof(FunctionEnvelopeBuilder.ForbiddenContentException), 400, "FUNCTION_INVOKE_FORBIDDEN_CONTENT")]
        public async Task Each_typed_refusal_has_its_status_and_code(Type exception, int status, string code)
        {
            GivenRequest();
            ServiceThrows((Exception)Activator.CreateInstance(exception, "why")!);

            var result = await _controller.Invoke("fn_1", "orders/42", wait: false);

            var outcome = Outcome(result);
            outcome.Status.Should().Be(status);
            outcome.Code.Should().Be(code);
            var value = ((ObjectResult)result).Value!;
            value.GetType().GetProperty("message")!.GetValue(value).Should().Be("why");
            value.GetType().GetProperty("instance")!.GetValue(value).Should().Be("/api/fn/fn_1/orders/42");
        }

        // ------------------------------------------------------------ successes ----

        [Fact]
        public async Task The_other_method_is_405_and_names_the_one_the_function_takes()
        {
            GivenRequest(method: "GET", body: null, contentType: null);
            ServiceThrows(new FunctionMethodNotAllowedException("POST"));

            var result = await _controller.Invoke("fn_1", null, wait: false);

            Outcome(result).Should().Be((405, "FUNCTION_INVOKE_METHOD_NOT_ALLOWED"));
            _controller.Response.Headers.Allow.ToString().Should().Be("POST");
        }

        [Fact]
        public async Task A_queued_run_is_202_and_a_finished_one_200()
        {
            GivenRequest();
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "QUEUED" });
            (await _controller.Invoke("fn_1", "orders/42", wait: false)).Should().BeOfType<AcceptedResult>();

            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "RUNNING" });
            (await _controller.Invoke("fn_1", "orders/42", wait: true)).Should().BeOfType<AcceptedResult>();

            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "SUCCEEDED", Result = "{}" });
            (await _controller.Invoke("fn_1", "orders/42", wait: true)).Should().BeOfType<OkObjectResult>();
        }

        // ------------------------------------------------- what the service gets ----

        [Fact]
        public async Task The_whole_request_is_handed_over_for_the_handlers_input()
        {
            GivenRequest(method: "PUT", query: "?limit=10&tag=a&tag=b&wait=true", more: r =>
            {
                r.Headers["Authorization"] = "Bearer t";
                r.Headers["Accept"] = "application/json";
            });
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "QUEUED" });

            await _controller.Invoke("fn_1", "orders/42", wait: false);

            _seen.Should().NotBeNull();
            _seen!.Method.Should().Be("PUT");
            _seen.Path.Should().Be("orders/42");
            _seen.Query!["limit"].Should().Equal("10");
            _seen.Query["tag"].Should().Equal("a", "b");
            // The platform's own parameter is consumed here, not handed to the handler. (Its value
            // reaches the action through [FromQuery] model binding, which a direct call bypasses —
            // the Wait flag itself is covered by Wait_comes_from_the_query_or_the_Prefer_header.)
            _seen.Query.Should().NotContainKey("wait");
            _seen.ContentType.Should().Be("application/json");
            Encoding.UTF8.GetString(_seen.Body!).Should().Be("{\"qty\":2}");
            _seen.BodyTooLarge.Should().BeFalse();
            // Every header travels to the service; the builder's allow-list is what filters. The
            // controller does not pre-filter so that one list stays the single source of truth.
            _seen.Headers!.Keys.Should().Contain("Authorization").And.Contain("Accept");
        }

        [Fact]
        public async Task Wait_comes_from_the_query_or_the_Prefer_header()
        {
            GivenRequest();
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "QUEUED" });

            await _controller.Invoke("fn_1", null, wait: true);
            _seen!.Wait.Should().BeTrue();

            GivenRequest(more: r => r.Headers["Prefer"] = "wait=10");
            await _controller.Invoke("fn_1", null, wait: false);
            _seen!.Wait.Should().BeTrue();

            GivenRequest();
            await _controller.Invoke("fn_1", null, wait: false);
            _seen!.Wait.Should().BeFalse();
        }

        [Fact]
        public async Task A_GET_with_no_body_hands_over_a_null_body()
        {
            GivenRequest(method: "GET", body: null, contentType: null);
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "QUEUED" });

            await _controller.Invoke("fn_1", "orders", wait: false);

            _seen!.Body.Should().BeNull();
            _seen.ContentType.Should().BeNull();
        }

        [Fact]
        public async Task An_oversized_body_is_not_buffered_and_is_flagged_for_the_service()
        {
            // The size verdict is the service's, after authorization, so an unauthorized caller
            // cannot use the cap as a probe. The controller's job is to stop reading and say so.
            var big = new string('x', (int)FunctionHttpInputBuilder.MaxBodyBytes + 1);
            GivenRequest(body: big, contentType: "text/plain");
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "QUEUED" });

            await _controller.Invoke("fn_1", null, wait: false);

            _seen!.BodyTooLarge.Should().BeTrue();
            _seen.Body.Should().BeNull();
        }

        [Fact]
        public async Task A_declared_length_over_the_cap_is_refused_without_reading()
        {
            GivenRequest(body: "small", contentType: "text/plain", more: r => r.ContentLength = FunctionHttpInputBuilder.MaxBodyBytes + 1);
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "QUEUED" });

            await _controller.Invoke("fn_1", null, wait: false);

            _seen!.BodyTooLarge.Should().BeTrue();
        }
    }
}
