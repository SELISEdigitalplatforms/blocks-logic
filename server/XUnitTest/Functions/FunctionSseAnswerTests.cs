using System.Text;
using BlocksTemplate.Api.Controllers;
using FluentAssertions;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Queue;
using Functions.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// What an SSE caller of a sync function reads: exactly two events — <c>run</c> with the run
    /// id at once, then <c>done</c> with the full response, or <c>error</c>.
    /// </summary>
    public class FunctionSseAnswerTests
    {
        private sealed class ListReader(params FunctionStreamPiece[] pieces) : IFunctionStreamReader
        {
            public async IAsyncEnumerable<FunctionStreamPiece> ReadAsync(
                string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (var piece in pieces)
                {
                    await Task.Yield();
                    yield return piece;
                }
            }
        }

        private static async Task<(string Body, HttpResponse Response)> AnswerAsync(
            InvokeResultDto answer, IFunctionStreamReader? reader = null, string? pollToken = "tok_1")
        {
            var invocation = new Mock<IFunctionInvocationService>();
            invocation.Setup(s => s.AwaitSseAnswerAsync("tenant-abc", "run_1", 35, It.IsAny<CancellationToken>()))
                .ReturnsAsync(answer);
            var http = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection()
                    .AddSingleton(invocation.Object)
                    .AddSingleton(reader ?? new ListReader())
                    .BuildServiceProvider(),
            };
            var body = new MemoryStream();
            http.Response.Body = body;

            await new FunctionSseAnswerResult("tenant-abc", "run_1", 35, _ => Task.FromResult(pollToken))
                .ExecuteResultAsync(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));

            return (Encoding.UTF8.GetString(body.ToArray()), http.Response);
        }

        private static string[] Events(string body) =>
            body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Where(e => !e.StartsWith(':')).ToArray();

        [Fact]
        public async Task A_returned_value_is_the_run_event_then_done_with_it_as_json()
        {
            var (body, response) = await AnswerAsync(new InvokeResultDto
            {
                RunId = "run_1", Status = FunctionQueueKeys.Wire.Succeeded, Result = "{\"ok\":true,\"n\":2}",
            });

            response.ContentType.Should().StartWith("text/event-stream");
            response.Headers["x-blocks-run-id"].ToString().Should().Be("run_1");
            Events(body).Should().Equal(
                "event: run\ndata: {\"runId\":\"run_1\"}",
                "event: done\ndata: {\"runId\":\"run_1\",\"status\":\"SUCCEEDED\",\"result\":{\"ok\":true,\"n\":2}}");
        }

        [Fact]
        public async Task A_streamed_answer_is_joined_into_one_result_not_sent_piece_by_piece()
        {
            var reader = new ListReader(
                new FunctionStreamPiece("Hel"), new FunctionStreamPiece("lo\nworld"),
                new FunctionStreamPiece(null, FunctionQueueKeys.Wire.Succeeded));

            var (body, _) = await AnswerAsync(new InvokeResultDto { RunId = "run_1", Streaming = true }, reader);

            Events(body).Should().Equal(
                "event: run\ndata: {\"runId\":\"run_1\"}",
                "event: done\ndata: {\"runId\":\"run_1\",\"status\":\"SUCCEEDED\",\"result\":\"Hello\\nworld\"}");
        }

        [Fact]
        public async Task A_stream_that_fails_ends_with_error()
        {
            var reader = new ListReader(
                new FunctionStreamPiece("part"),
                new FunctionStreamPiece(null, FunctionQueueKeys.Wire.Failed, "USER_RUNTIME_ERROR", "boom"));

            var (body, _) = await AnswerAsync(new InvokeResultDto { RunId = "run_1", Streaming = true }, reader);

            Events(body).Should().HaveCount(2);
            Events(body)[1].Should().Be(
                "event: error\ndata: {\"runId\":\"run_1\",\"status\":\"FAILED\",\"errorCode\":\"USER_RUNTIME_ERROR\",\"message\":\"boom\"}");
        }

        [Fact]
        public async Task A_failed_run_ends_with_error()
        {
            var (body, _) = await AnswerAsync(new InvokeResultDto
            {
                RunId = "run_1", Status = FunctionQueueKeys.Wire.TimedOut, ErrorCode = "TimedOut", ErrorMessage = "too slow",
            });

            Events(body)[1].Should().StartWith("event: error\n").And.Contain("\"errorCode\":\"TimedOut\"");
        }

        [Fact]
        public async Task A_run_not_finished_in_the_wait_ends_with_still_running_and_a_poll_token()
        {
            var (body, _) = await AnswerAsync(new InvokeResultDto { RunId = "run_1", Status = FunctionQueueKeys.Wire.Queued });

            var events = Events(body);
            events.Should().HaveCount(2);
            events[1].Should().StartWith("event: error\n")
                .And.Contain("\"errorCode\":\"STILL_RUNNING\"")
                .And.Contain("\"pollToken\":\"tok_1\"");
        }

        [Fact]
        public async Task A_failure_while_waiting_still_ends_with_an_error_event()
        {
            var invocation = new Mock<IFunctionInvocationService>();
            invocation.Setup(s => s.AwaitSseAnswerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));
            var http = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton(invocation.Object).BuildServiceProvider(),
            };
            var body = new MemoryStream();
            http.Response.Body = body;
            // DefaultHttpContext reports HasStarted only for a real server; the test body writes stand in.
            http.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponse());

            await new FunctionSseAnswerResult("tenant-abc", "run_1", 35, _ => Task.FromResult<string?>("tok_1"))
                .ExecuteResultAsync(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));

            var events = Events(Encoding.UTF8.GetString(body.ToArray()));
            events.Should().HaveCount(2);
            events[1].Should().Contain("\"errorCode\":\"UNAVAILABLE\"").And.Contain("\"pollToken\":\"tok_1\"");
        }

        private sealed class StartedResponse : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
        {
            public override bool HasStarted => true;
        }

        [Fact]
        public async Task Keep_alives_are_comments_not_events()
        {
            var invocation = new Mock<IFunctionInvocationService>();
            invocation.Setup(s => s.AwaitSseAnswerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await Task.Delay(150);
                    return new InvokeResultDto { RunId = "run_1", Status = FunctionQueueKeys.Wire.Succeeded, Result = "1" };
                });
            var http = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton(invocation.Object).BuildServiceProvider(),
            };
            var body = new MemoryStream();
            http.Response.Body = body;

            await new FunctionSseAnswerResult("tenant-abc", "run_1", 35, _ => Task.FromResult<string?>(null), TimeSpan.FromMilliseconds(20))
                .ExecuteResultAsync(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));

            var text = Encoding.UTF8.GetString(body.ToArray());
            text.Should().Contain(": keep-alive\n\n");
            Events(text).Should().HaveCount(2);
            text.Should().EndWith("event: done\ndata: {\"runId\":\"run_1\",\"status\":\"SUCCEEDED\",\"result\":1}\n\n");
        }
    }
}
