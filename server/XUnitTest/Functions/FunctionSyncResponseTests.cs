using System.Text;
using System.Text.Json;
using BlocksTemplate.Api.Controllers;
using Common.InternalService.Access;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Synchronous answers on the public route (sandbox/REUSE.md, "Answer mapping"): how a finished
    /// run becomes the HTTP response, which headers a function may set, and — above all — that a
    /// caller who did not ask for sync, or whose run did not finish in time, gets exactly the 202
    /// it always got.
    /// </summary>
    public class FunctionSyncResponseTests
    {
        // ================================================================ the mapper ====

        private static InvokeResultDto Finished(string? result, string status = "SUCCEEDED") =>
            new() { RunId = "run_1", Status = status, Result = result, RespondSynchronously = true };

        private static FunctionHttpResponseMapper.Response Map(string? result, string status = "SUCCEEDED") =>
            FunctionHttpResponseMapper.Map(Finished(result, status));

        private static string Text(FunctionHttpResponseMapper.Response r) => Encoding.UTF8.GetString(r.Body);

        private static string[] Header(FunctionHttpResponseMapper.Response r, string name) =>
            r.Headers.Where(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToArray();

        [Fact]
        public void A_status_shaped_result_with_a_string_body_is_sent_as_is_with_its_content_type()
        {
            var r = Map("""{"statusCode":201,"headers":{"content-type":"text/html; charset=utf-8","x-trace":"t1"},"body":"<b>hi</b>"}""");

            r.StatusCode.Should().Be(201);
            r.ContentType.Should().Be("text/html; charset=utf-8");
            Text(r).Should().Be("<b>hi</b>");
            Header(r, "x-trace").Should().Equal("t1");
            Header(r, "content-type").Should().BeEmpty("the content type travels once, as ContentType");
        }

        [Fact]
        public void A_string_body_without_a_content_type_is_plain_text()
        {
            var r = Map("""{"statusCode":200,"body":"pong"}""");

            r.ContentType.Should().Be("text/plain; charset=utf-8");
            Text(r).Should().Be("pong");
        }

        [Fact]
        public void A_structured_body_is_sent_as_json()
        {
            var r = Map("""{"statusCode":404,"body":{"error":"no such order"}}""");

            r.StatusCode.Should().Be(404);
            r.ContentType.Should().Be("application/json; charset=utf-8");
            JsonDocument.Parse(r.Body).RootElement.GetProperty("error").GetString().Should().Be("no such order");
        }

        [Fact]
        public void A_status_shaped_result_without_a_body_has_an_empty_body()
        {
            var r = Map("""{"statusCode":202}""");

            r.StatusCode.Should().Be(202);
            r.Body.Should().BeEmpty();
        }

        [Theory]
        [InlineData(204)]
        [InlineData(304)]
        public void A_bodyless_status_never_carries_a_body_even_if_the_function_sent_one(int status)
        {
            var r = Map($$"""{"statusCode":{{status}},"body":"ignored"}""");

            r.StatusCode.Should().Be(status);
            r.Body.Should().BeEmpty("Kestrel throws when a body is written for this status");
        }

        [Theory]
        [InlineData("""{"ok":true}""")]
        [InlineData("""[1,2,3]""")]
        [InlineData("42")]
        [InlineData("\"just text\"")]
        [InlineData("""{"statusCode":"200","body":"x"}""")]
        [InlineData("""{"statusCode":99}""")]
        [InlineData("""{"statusCode":600}""")]
        [InlineData("""{"statusCode":200.5}""")]
        public void Anything_else_is_200_json_with_the_value_untouched(string result)
        {
            var r = Map(result);

            r.StatusCode.Should().Be(200);
            r.ContentType.Should().Be("application/json; charset=utf-8");
            Text(r).Should().Be(result);
        }

        [Fact]
        public void A_handler_that_returned_nothing_is_200_json_null()
        {
            var r = Map(null);

            r.StatusCode.Should().Be(200);
            Text(r).Should().Be("null");
        }

        [Fact]
        public void A_1xx_status_cannot_be_a_final_answer_and_is_a_502()
        {
            var r = Map("""{"statusCode":101,"body":"upgrade"}""");

            r.StatusCode.Should().Be(502);
            JsonDocument.Parse(r.Body).RootElement.GetProperty("error").GetProperty("code").GetString()
                .Should().Be("INVALID_STATUS_CODE");
        }

        [Theory]
        [InlineData("Connection")]
        [InlineData("keep-alive")]
        [InlineData("Transfer-Encoding")]
        [InlineData("content-length")]
        [InlineData("Upgrade")]
        [InlineData("Server")]
        [InlineData("Access-Control-Allow-Origin")]
        [InlineData("Strict-Transport-Security")]
        [InlineData("WWW-Authenticate")]
        [InlineData("X-Forwarded-For")]
        [InlineData("x-forwarded-host")]
        [InlineData("X-Real-IP")]
        [InlineData("X-Original-URL")]
        [InlineData("x-blocks-key")]
        [InlineData("Set-Cookie")]
        public void Anything_off_the_allow_list_is_dropped(string name)
        {
            var r = Map($$"""{"statusCode":200,"headers":{"{{name}}":"x","x-kept":"1"},"body":"b"}""");

            Header(r, name).Should().BeEmpty();
            Header(r, "x-kept").Should().Equal("1");
        }

        [Theory]
        [InlineData("Content-Language")]
        [InlineData("Content-Disposition")]
        [InlineData("Cache-Control")]
        [InlineData("Expires")]
        [InlineData("Last-Modified")]
        [InlineData("ETag")]
        [InlineData("Vary")]
        [InlineData("Retry-After")]
        [InlineData("X-Trace-Id")]
        public void The_allowed_headers_pass(string name)
            => Header(Map($$"""{"statusCode":200,"headers":{"{{name}}":"v1"},"body":"b"}"""), name).Should().Equal("v1");

        [Fact]
        public void Set_cookie_is_always_dropped()
        {
            var r = Map("""{"statusCode":200,"headers":{"Set-Cookie":["a=1; Path=/","b=2"]},"body":"b"}""");

            Header(r, "set-cookie").Should().BeEmpty();
        }

        [Theory]
        [InlineData(302, "https://example.com/next", true)]
        [InlineData(301, "http://example.com/", true)]
        [InlineData(303, "/orders/9", true)]
        [InlineData(307, "orders/9?x=1", true)]
        [InlineData(302, "javascript:alert(1)", false)]
        [InlineData(302, "data:text/html,hi", false)]
        [InlineData(302, "//evil.example/", false)]
        [InlineData(302, "/\\evil.example/", false)]
        [InlineData(302, "ftp://example.com/", false)]
        [InlineData(200, "https://example.com/", false)]
        [InlineData(201, "/orders/9", false)]
        public void Location_passes_only_on_a_redirect_to_a_web_or_relative_target(int status, string location, bool kept)
        {
            var r = Map(JsonSerializer.Serialize(new { statusCode = status, headers = new Dictionary<string, string> { ["Location"] = location } }));

            if (kept) Header(r, "location").Should().Equal(location);
            else Header(r, "location").Should().BeEmpty();
        }

        [Fact]
        public void At_most_32_headers_pass()
        {
            var headers = Enumerable.Range(0, 40).ToDictionary(i => $"x-h{i}", i => "v");
            var r = Map(JsonSerializer.Serialize(new { statusCode = 200, headers, body = "b" }));

            r.Headers.Count(h => h.Key.StartsWith("x-h", StringComparison.Ordinal)).Should().Be(32);
        }

        [Fact]
        public void At_most_8_kb_of_header_names_and_values_pass()
        {
            var headers = Enumerable.Range(0, 10).ToDictionary(i => $"x-big{i}", i => new string('a', 2000));
            var r = Map(JsonSerializer.Serialize(new { statusCode = 200, headers, body = "b" }));

            var forwarded = r.Headers.Where(h => h.Key.StartsWith("x-big", StringComparison.Ordinal)).ToList();
            forwarded.Should().HaveCount(4);
            forwarded.Sum(h => h.Key.Length + h.Value.Length).Should().BeLessThanOrEqualTo(8 * 1024);
        }

        [Fact]
        public void A_header_value_with_a_line_break_or_a_bad_name_is_dropped_not_a_500()
        {
            var r = Map("""{"statusCode":200,"headers":{"x-split":"a\r\nSet-Cookie: s=1","bad name":"v","x-ok":"fine","x-num":5,"x-obj":{"a":1}},"body":"b"}""");

            Header(r, "x-split").Should().BeEmpty();
            Header(r, "bad name").Should().BeEmpty();
            Header(r, "set-cookie").Should().BeEmpty();
            Header(r, "x-obj").Should().BeEmpty();
            Header(r, "x-ok").Should().Equal("fine");
            Header(r, "x-num").Should().Equal("5");
        }

        [Fact]
        public void Every_answer_is_served_with_nosniff_and_a_sandboxing_csp()
        {
            foreach (var r in new[] { Map("""{"ok":1}"""), Map("""{"statusCode":200,"headers":{"x-content-type-options":"sniff"},"body":"<p>"}"""), Map(null, status: "FAILED") })
            {
                Header(r, "x-content-type-options").Should().Equal("nosniff");
                Header(r, "content-security-policy").Should().Contain("sandbox");
            }
        }

        [Theory]
        [InlineData("FAILED", "UserRuntimeError")]
        [InlineData("CANCELLED", null)]
        [InlineData("RESOURCE_EXCEEDED", "MemoryLimit")]
        public void A_failed_run_is_a_502_with_the_error_shape(string status, string? code)
        {
            var result = new InvokeResultDto { RunId = "r", Status = status, ErrorCode = code, ErrorMessage = "boom" };

            var r = FunctionHttpResponseMapper.Map(result);

            r.StatusCode.Should().Be(502);
            r.ContentType.Should().Be("application/json; charset=utf-8");
            var error = JsonDocument.Parse(r.Body).RootElement.GetProperty("error");
            error.GetProperty("code").GetString().Should().Be(code ?? status);
            error.GetProperty("message").GetString().Should().Be("boom");
            JsonDocument.Parse(r.Body).RootElement.GetProperty("runId").GetString().Should().Be("r");
        }

        [Fact]
        public void A_timed_out_run_is_a_504()
        {
            var r = FunctionHttpResponseMapper.Map(
                new InvokeResultDto { RunId = "r", Status = "TIMED_OUT", ErrorCode = "TimedOut" });

            r.StatusCode.Should().Be(504);
            JsonDocument.Parse(r.Body).RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("TimedOut");
            JsonDocument.Parse(r.Body).RootElement.GetProperty("runId").GetString().Should().Be("r");
        }

        [Fact]
        public void A_function_that_answered_but_whose_output_action_failed_still_gives_its_answer()
            => Map("""{"statusCode":201,"body":"made"}""", status: "OUTPUT_FAILED").StatusCode.Should().Be(201);

        // ============================================================== Prefer parsing ====

        [Theory]
        [InlineData("wait=10", false, 10)]
        [InlineData("WAIT=5", false, 5)]
        [InlineData("respond-async", true, null)]
        [InlineData("respond-async, wait=10", true, 10)]
        [InlineData("return=minimal; foo=bar, wait=\"7\"", false, 7)]
        [InlineData("wait=soon", false, null)]
        [InlineData("wait=-1", false, null)]
        [InlineData("handling=lenient", false, null)]
        [InlineData("", false, null)]
        public void Prefer_is_read_the_rfc_7240_way(string header, bool respondAsync, int? wait)
            => FunctionsController.ParsePrefer(new StringValues(header)).Should().Be((respondAsync, wait));

        [Fact]
        public void Prefer_across_several_header_lines_is_read_whole()
            => FunctionsController.ParsePrefer(new StringValues(["wait=3", "respond-async"])).Should().Be((true, 3));

        // ============================================================== the controller ====

        private readonly Mock<IFunctionInvocationService> _invocation = new();
        private readonly Mock<IEndpointAccessAuthorizer> _access = new();
        private readonly Mock<IFunctionPollTokenService> _pollTokens = new();
        private InvokeFunctionRequestDto? _seen;

        private FunctionsController Controller(Action<HttpRequest>? more = null)
        {
            var controller = new FunctionsController(
                new Mock<IFunctionService>().Object,
                new Mock<IFunctionDeploymentService>().Object,
                _invocation.Object,
                new Mock<IFunctionRunService>().Object,
                new Mock<IFunctionBuildService>().Object,
                new Mock<IFunctionAuditService>().Object,
                _access.Object,
                _pollTokens.Object,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FunctionsController>.Instance);

            var http = new DefaultHttpContext();
            http.Request.Method = "POST";
            http.Request.Path = "/api/fn/fn_1/orders";
            var bytes = Encoding.UTF8.GetBytes("{\"qty\":2}");
            http.Request.Body = new MemoryStream(bytes);
            http.Request.ContentLength = bytes.Length;
            http.Request.ContentType = "application/json";
            more?.Invoke(http.Request);
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            _access.Setup(a => a.ResolveTenantIdAsync(It.IsAny<HttpRequest>())).ReturnsAsync("tenant-abc");
            _pollTokens.Setup(p => p.IssueAsync("tenant-abc", "run_1", It.IsAny<CancellationToken>())).ReturnsAsync("tok");
            return controller;
        }

        private void ServiceAnswers(InvokeResultDto result) =>
            _invocation.Setup(s => s.InvokeHttpAsync("tenant-abc", "fn_1", It.IsAny<InvokeFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, InvokeFunctionRequestDto, CancellationToken>((_, _, r, _) => _seen = r)
                .ReturnsAsync(result);

        [Fact]
        public async Task The_Prefer_header_reaches_the_service_which_decides_whether_to_wait()
        {
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "QUEUED" });

            await Controller(r => r.Headers["Prefer"] = "wait=12").Invoke("fn_1", "orders");
            _seen!.PreferWaitSeconds.Should().Be(12);
            _seen.PreferAsync.Should().BeFalse();
            _seen.Wait.Should().BeFalse("the controller never decides on its own");

            await Controller(r => r.Headers["Prefer"] = "respond-async").Invoke("fn_1", "orders");
            _seen!.PreferAsync.Should().BeTrue();
            _seen.PreferWaitSeconds.Should().BeNull();

            await Controller().Invoke("fn_1", "orders");
            _seen!.PreferAsync.Should().BeFalse();
            _seen.PreferWaitSeconds.Should().BeNull();
        }

        [Fact]
        public async Task A_sync_run_that_finished_is_answered_with_the_functions_own_response()
        {
            ServiceAnswers(new InvokeResultDto
            {
                RunId = "run_1", Status = "SUCCEEDED", RespondSynchronously = true,
                Result = """{"statusCode":201,"headers":{"x-order":"/orders/9","location":"/orders/9"},"body":{"id":9}}""",
            });
            var controller = Controller(r => r.Headers["Prefer"] = "wait=10");

            var result = await controller.Invoke("fn_1", "orders");

            var answer = result.Should().BeOfType<FunctionSyncAnswerResult>().Subject.Answer;
            answer.StatusCode.Should().Be(201);

            // And it is written exactly: status, headers, bytes.
            var http = controller.ControllerContext.HttpContext;
            http.Response.Body = new MemoryStream();
            await result.ExecuteResultAsync(controller.ControllerContext);
            http.Response.StatusCode.Should().Be(201);
            http.Response.Headers["x-order"].ToString().Should().Be("/orders/9");
            http.Response.Headers.Location.ToString().Should().BeEmpty("Location passes on a 3xx only");
            http.Response.ContentType.Should().Be("application/json; charset=utf-8");
            Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray()).Should().Be("{\"id\":9}");
            _pollTokens.Verify(p => p.IssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Several_values_of_one_header_are_all_written()
        {
            ServiceAnswers(new InvokeResultDto
            {
                RunId = "run_1", Status = "SUCCEEDED", RespondSynchronously = true,
                Result = """{"statusCode":200,"headers":{"x-tag":["a","b"],"set-cookie":"s=1"},"body":"ok"}""",
            });
            var controller = Controller();

            var result = await controller.Invoke("fn_1", "orders");
            controller.ControllerContext.HttpContext.Response.Body = new MemoryStream();
            await result.ExecuteResultAsync(controller.ControllerContext);

            controller.ControllerContext.HttpContext.Response.Headers["x-tag"].ToArray().Should().Equal("a", "b");
            controller.ControllerContext.HttpContext.Response.Headers.SetCookie.Should().BeEmpty();
        }

        [Fact]
        public async Task A_caller_that_hung_up_gets_an_empty_result_not_an_error()
        {
            using var gone = new CancellationTokenSource();
            var controller = Controller();
            controller.ControllerContext.HttpContext.RequestAborted = gone.Token;
            _invocation.Setup(s => s.InvokeHttpAsync("tenant-abc", "fn_1", It.IsAny<InvokeFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, string _, InvokeFunctionRequestDto _, CancellationToken ct) =>
                {
                    await gone.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                    return new InvokeResultDto();
                });

            var result = await controller.Invoke("fn_1", "orders");

            result.Should().BeOfType<EmptyResult>();
        }

        [Fact]
        public async Task A_cancellation_that_is_not_the_caller_leaving_still_surfaces()
        {
            _invocation.Setup(s => s.InvokeHttpAsync("tenant-abc", "fn_1", It.IsAny<InvokeFunctionRequestDto>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            var act = () => Controller().Invoke("fn_1", "orders");

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Theory]
        [InlineData("FAILED", 502)]
        [InlineData("TIMED_OUT", 504)]
        public async Task A_sync_run_that_failed_is_a_gateway_error(string status, int expected)
        {
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = status, ErrorCode = "X", ErrorMessage = "m", RespondSynchronously = true });

            var result = await Controller().Invoke("fn_1", "orders");

            result.Should().BeOfType<FunctionSyncAnswerResult>().Which.Answer.StatusCode.Should().Be(expected);
        }

        [Theory]
        [InlineData("QUEUED")]
        [InlineData("RUNNING")]
        [InlineData("OUTPUT_PROCESSING")]
        public async Task A_sync_run_not_finished_in_time_is_the_same_202_an_async_caller_gets(string status)
        {
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = status, RespondSynchronously = true });
            var sync = await Controller().Invoke("fn_1", "orders");

            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = status });
            var async = await Controller().Invoke("fn_1", "orders");

            var syncBody = sync.Should().BeOfType<AcceptedResult>().Which.Value.Should().BeOfType<InvokeResultDto>().Subject;
            syncBody.PollToken.Should().Be("tok");
            // Byte-for-byte: the sync-only flags never reach the wire.
            JsonSerializer.Serialize(syncBody).Should().Be(JsonSerializer.Serialize(((AcceptedResult)async).Value));
            JsonSerializer.Serialize(syncBody).Should().NotContain("RespondSynchronously").And.NotContain("AllowSetCookie");
        }

        [Fact]
        public async Task Without_sync_a_finished_run_keeps_its_old_200_shape()
        {
            // Nothing changes for a caller who did not ask: the service never sets the flag then.
            ServiceAnswers(new InvokeResultDto { RunId = "run_1", Status = "SUCCEEDED", Result = """{"statusCode":201}""" });

            var result = await Controller().Invoke("fn_1", "orders");

            result.Should().BeOfType<OkObjectResult>();
        }
    }
}
