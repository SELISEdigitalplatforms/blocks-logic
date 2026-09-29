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
        private readonly Mock<IFunctionPollTokenService> _pollTokens = new();
        private readonly Mock<IFunctionRunService> _runs = new();
        private readonly FunctionsController _controller;

        public FunctionsControllerInvokeTests()
        {
            _controller = new FunctionsController(
                new Mock<IFunctionService>().Object,
                new Mock<IFunctionDeploymentService>().Object,
                _invocation.Object,
                _runs.Object,
                new Mock<IFunctionBuildService>().Object,
                new Mock<IFunctionAuditService>().Object,
                _access.Object,
                _pollTokens.Object,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FunctionsController>.Instance);
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

        [Theory]
        [InlineData("CLAIMED")]
        [InlineData("STARTING")]
        [InlineData("OUTPUT_PROCESSING")]
        public async Task Every_unfinished_run_status_is_202_not_just_queued_and_running(string status)
        {
            // A wait that lapses while the runner holds the run used to answer 200 with no result.
            GivenRequest();
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = status });
            (await _controller.Invoke("fn_1", "orders/42", wait: true)).Should().BeOfType<AcceptedResult>();
        }

        [Theory]
        [InlineData("FAILED")]
        [InlineData("TIMED_OUT")]
        [InlineData("CANCELLED")]
        [InlineData("OUTPUT_FAILED")]
        public async Task Every_finished_run_status_is_200(string status)
        {
            GivenRequest();
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = status });
            (await _controller.Invoke("fn_1", "orders/42", wait: true)).Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task A_queue_that_refuses_the_run_is_503_with_retry_after()
        {
            GivenRequest();
            ServiceThrows(new FunctionUnavailableException("the run queue is unavailable", "run_1", 7));

            var result = await _controller.Invoke("fn_1", "orders/42", wait: false);

            Outcome(result).Should().Be((503, "FUNCTION_INVOKE_UNAVAILABLE"));
            _controller.Response.Headers.RetryAfter.ToString().Should().Be("7");
        }

        [Fact]
        public async Task Test_maps_a_refused_enqueue_to_503_with_retry_after()
        {
            GivenStudioRequest();
            _invocation.Setup(s => s.TestAsync(It.IsAny<string>(), "fn_1", It.IsAny<TestFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionUnavailableException("down", "run_9", 4));

            var result = await _controller.Test(new TestFunctionRequestDto { FunctionId = "fn_1" });

            result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
            _controller.Response.Headers.RetryAfter.ToString().Should().Be("4");
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

        // ------------------------------------------------------------ rate limit ----

        [Fact]
        public async Task A_rate_limited_invoke_is_429_with_retry_after_not_400()
        {
            GivenRequest();
            ServiceThrows(new FunctionRateLimitedException("this function allows 10 requests per minute", 18));

            var result = await _controller.Invoke("fn_1", "orders/42", wait: false);

            Outcome(result).Should().Be((429, "FUNCTION_INVOKE_RATE_LIMITED"));
            _controller.Response.Headers.RetryAfter.ToString().Should().Be("18");
        }

        [Fact]
        public void Retry_after_is_at_least_one_second()
            => new FunctionRateLimitedException("x", 0).RetryAfterSeconds.Should().Be(1);

        // ------------------------------------------------------------ poll token ----

        [Fact]
        public async Task A_202_carries_a_poll_token_issued_for_that_run_and_tenant()
        {
            GivenRequest();
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "QUEUED" });
            _pollTokens.Setup(p => p.IssueAsync("tenant-abc", "run_1", It.IsAny<CancellationToken>())).ReturnsAsync("tok");

            var result = await _controller.Invoke("fn_1", "orders/42", wait: false);

            result.Should().BeOfType<AcceptedResult>()
                .Which.Value.Should().BeOfType<InvokeResultDto>().Which.PollToken.Should().Be("tok");
        }

        [Fact]
        public async Task A_finished_run_gets_no_poll_token()
        {
            GivenRequest();
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "SUCCEEDED", Result = "{}" });

            var result = await _controller.Invoke("fn_1", "orders/42", wait: true);

            ((OkObjectResult)result).Value.Should().BeOfType<InvokeResultDto>().Which.PollToken.Should().BeNull();
            _pollTokens.Verify(p => p.IssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_token_store_failure_still_answers_202_because_the_run_is_already_queued()
        {
            // A 5xx here would invite a retry that queues the same work twice.
            GivenRequest();
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "QUEUED" });
            _pollTokens.Setup(p => p.IssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));

            var result = await _controller.Invoke("fn_1", "orders/42", wait: false);

            result.Should().BeOfType<AcceptedResult>()
                .Which.Value.Should().BeOfType<InvokeResultDto>().Which.PollToken.Should().BeNull();
        }

        private const string RunGuid = "2f4b8f3e-8e0c-4a3e-9d1f-0c3c3f6b2a11";

        private void GivenPoll(string? tenant = "tenant-abc", string? token = "tok")
        {
            var http = new DefaultHttpContext();
            http.Request.Method = "GET";
            http.Request.Path = $"/api/fn/runs/{RunGuid}/result";
            if (tenant is not null) http.Request.Headers["x-blocks-key"] = tenant;
            if (token is not null) http.Request.Headers[FunctionsController.PollTokenHeader] = token;
            _controller.ControllerContext = new ControllerContext { HttpContext = http };
            _access.Setup(a => a.ResolveTenantIdAsync(It.IsAny<HttpRequest>())).ReturnsAsync(tenant);
        }

        [Fact]
        public async Task Anonymous_poll_with_the_right_token_returns_status_and_result_only()
        {
            GivenPoll();
            _pollTokens.Setup(p => p.VerifyAsync("tenant-abc", RunGuid, "tok", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _runs.Setup(r => r.GetPollResultAsync("tenant-abc", RunGuid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InvokeResultDto { RunId = RunGuid, Status = "SUCCEEDED", Result = "{\"ok\":1}" });

            var result = await _controller.PollRunResult(RunGuid);

            var dto = result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<InvokeResultDto>().Subject;
            dto.Result.Should().Be("{\"ok\":1}");
            // The poll shape has nowhere to put the input or headers — pinned so nobody swaps in RunDetailDto.
            typeof(InvokeResultDto).GetProperty("Input").Should().BeNull();
            _runs.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Anonymous_poll_without_a_tenant_is_401()
        {
            GivenPoll(tenant: null);

            Outcome(await _controller.PollRunResult(RunGuid)).Should().Be((401, "FUNCTION_INVOKE_UNAUTHORIZED"));
            _pollTokens.Verify(p => p.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("wrong")]
        [InlineData("")]
        public async Task Anonymous_poll_with_a_missing_or_wrong_token_is_404_and_reads_nothing(string? token)
        {
            GivenPoll(token: token);
            _pollTokens.Setup(p => p.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            Outcome(await _controller.PollRunResult(RunGuid)).Should().Be((404, "FUNCTION_INVOKE_NOT_FOUND"));
            _runs.Verify(r => r.GetPollResultAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("not-a-guid")]
        [InlineData("../../etc")]
        [InlineData("*")]
        public async Task Anonymous_poll_for_a_malformed_run_id_is_404_before_any_lookup(string runId)
        {
            GivenPoll();

            Outcome(await _controller.PollRunResult(runId)).Should().Be((404, "FUNCTION_INVOKE_NOT_FOUND"));
            _pollTokens.Verify(p => p.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Anonymous_poll_for_a_run_that_is_gone_is_the_same_404()
        {
            GivenPoll();
            _pollTokens.Setup(p => p.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _runs.Setup(r => r.GetPollResultAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionNotFoundException("run 'x' was not found"));

            var result = await _controller.PollRunResult(RunGuid);

            Outcome(result).Should().Be((404, "FUNCTION_INVOKE_NOT_FOUND"));
            var value = ((ObjectResult)result).Value!;
            value.GetType().GetProperty("message")!.GetValue(value).Should().Be("No run with that id and poll token.");
        }

        [Fact]
        public async Task Anonymous_poll_when_the_token_store_is_down_is_503_not_a_leak_or_a_500()
        {
            GivenPoll();
            _pollTokens.Setup(p => p.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));

            Outcome(await _controller.PollRunResult(RunGuid)).Should().Be((503, "FUNCTION_INVOKE_UNAVAILABLE"));
        }

        // ------------------------------------------------ Studio test / replay ----

        private void GivenStudioRequest()
            => _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        [Fact]
        public async Task Test_maps_forbidden_content_to_400_instead_of_500()
        {
            GivenStudioRequest();
            _invocation.Setup(s => s.TestAsync(It.IsAny<string>(), "fn_1", It.IsAny<TestFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionEnvelopeBuilder.ForbiddenContentException("forbidden key"));

            var result = await _controller.Test(new TestFunctionRequestDto { FunctionId = "fn_1" });

            var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task Test_maps_a_rate_limit_to_429_with_retry_after()
        {
            GivenStudioRequest();
            _invocation.Setup(s => s.TestAsync(It.IsAny<string>(), "fn_1", It.IsAny<TestFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionRateLimitedException("limit", 30));

            var result = await _controller.Test(new TestFunctionRequestDto { FunctionId = "fn_1" });

            result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(429);
            _controller.Response.Headers.RetryAfter.ToString().Should().Be("30");
        }

        [Fact]
        public async Task Test_passes_a_result_through_unchanged()
        {
            GivenStudioRequest();
            var expected = new InvokeResultDto { RunId = "run_1", Status = "SUCCEEDED" };
            _invocation.Setup(s => s.TestAsync(It.IsAny<string>(), "fn_1", It.IsAny<TestFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            var result = await _controller.Test(new TestFunctionRequestDto { FunctionId = "fn_1" });

            result.Value.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task Replay_maps_forbidden_content_to_400_and_rate_limit_to_429()
        {
            GivenStudioRequest();
            _runs.Setup(r => r.ReplayAsync(It.IsAny<string>(), "run_1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionEnvelopeBuilder.ForbiddenContentException("forbidden key"));
            (await _controller.ReplayRun(new RunIdRequestDto { RunId = "run_1" }))
                .Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);

            _runs.Setup(r => r.ReplayAsync(It.IsAny<string>(), "run_1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionRateLimitedException("limit", 5));
            (await _controller.ReplayRun(new RunIdRequestDto { RunId = "run_1" }))
                .Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(429);
        }
    }
}
