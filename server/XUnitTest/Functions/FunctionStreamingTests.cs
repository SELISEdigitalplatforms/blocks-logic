using System.Text;
using FluentAssertions;
using Functions.DomainService.Queue;
using Functions.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Blocks.Genesis;
using StackExchange.Redis;
using BlocksTemplate.Api.Controllers;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A streamed answer (F-5) from the Api's side: reading the run's Redis stream in order to its
    /// end, and writing it to the caller as plain chunked text or as Server-Sent Events.
    /// </summary>
    public class FunctionStreamingTests
    {
        // ------------------------------------------------------------------ the reader ----

        private static StreamEntry Piece(string id, string data) => new(id, [new NameValueEntry(FunctionQueueKeys.StreamDataField, data)]);

        private static StreamEntry End(string id, string status, string code = "", string message = "") => new(id,
        [
            new NameValueEntry(FunctionQueueKeys.StreamEndField, status),
            new NameValueEntry(FunctionQueueKeys.StreamCodeField, code),
            new NameValueEntry(FunctionQueueKeys.StreamMessageField, message),
        ]);

        private static (FunctionStreamReader Reader, FakeRedisDatabase Fake) Reader(Queue<StreamEntry[]> batches, int maxSeconds = 120)
        {
            var (db, fake) = FakeRedisDatabase.Create();
            fake.On("StreamReadAsync", _ => batches.Count > 0 ? batches.Dequeue() : Array.Empty<StreamEntry>());
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(db);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Functions:StreamMaxSeconds"] = maxSeconds.ToString() })
                .Build();
            return (new FunctionStreamReader(cache.Object, configuration, NullLogger<FunctionStreamReader>.Instance), fake);
        }

        private static async Task<List<FunctionStreamPiece>> ReadAllAsync(FunctionStreamReader reader)
        {
            var pieces = new List<FunctionStreamPiece>();
            await foreach (var piece in reader.ReadAsync("run_1", CancellationToken.None)) pieces.Add(piece);
            return pieces;
        }

        [Fact]
        public async Task The_reader_yields_every_piece_in_order_and_stops_at_the_end()
        {
            var (reader, fake) = Reader(new Queue<StreamEntry[]>(
            [
                [Piece("1-0", "Hel"), Piece("2-0", "lo")],
                [],
                [Piece("3-0", "!"), End("4-0", FunctionQueueKeys.Wire.Succeeded)],
            ]));

            var pieces = await ReadAllAsync(reader);

            pieces.Where(p => !p.IsEnd).Select(p => p.Data).Should().Equal("Hel", "lo", "!");
            pieces.Last().EndStatus.Should().Be(FunctionQueueKeys.Wire.Succeeded);
            // Each read continues after the last id seen: nothing is read twice.
            fake.Calls("StreamReadAsync").Select(c => c[1]!.ToString()).Should().Equal("0-0", "2-0", "2-0");
        }

        [Fact]
        public async Task A_failed_end_carries_the_runs_code_and_message()
        {
            var (reader, _) = Reader(new Queue<StreamEntry[]>([[Piece("1-0", "a"), End("2-0", "FAILED", "USER_RUNTIME_ERROR", "upstream closed")]]));

            var end = (await ReadAllAsync(reader)).Last();

            end.EndStatus.Should().Be("FAILED");
            end.ErrorCode.Should().Be("USER_RUNTIME_ERROR");
            end.ErrorMessage.Should().Be("upstream closed");
        }

        [Fact]
        public async Task A_stream_that_never_ends_is_closed_as_timed_out()
        {
            var (reader, _) = Reader(new Queue<StreamEntry[]>([[Piece("1-0", "a")]]), maxSeconds: 1);

            var pieces = await ReadAllAsync(reader).WaitAsync(TimeSpan.FromSeconds(10));

            pieces.Last().EndStatus.Should().Be(FunctionQueueKeys.Wire.TimedOut);
        }

        // ---------------------------------------------------------------- the response ----

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

        private sealed class AbortFeature : IHttpRequestLifetimeFeature
        {
            public CancellationToken RequestAborted { get; set; }
            public bool Aborted { get; private set; }
            public void Abort() => Aborted = true;
        }

        private static async Task<(string Body, HttpResponse Response, AbortFeature Abort)> WriteAsync(bool sse, params FunctionStreamPiece[] pieces)
        {
            var http = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton<IFunctionStreamReader>(new ListReader(pieces)).BuildServiceProvider(),
            };
            var abort = new AbortFeature();
            http.Features.Set<IHttpRequestLifetimeFeature>(abort);
            var body = new MemoryStream();
            http.Response.Body = body;

            await new FunctionStreamResult("run_1", sse).ExecuteResultAsync(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));

            return (Encoding.UTF8.GetString(body.ToArray()), http.Response, abort);
        }

        [Fact]
        public async Task Plain_text_is_the_pieces_as_they_are()
        {
            var (body, response, abort) = await WriteAsync(false,
                new FunctionStreamPiece("Hel"), new FunctionStreamPiece("lo\n"), new FunctionStreamPiece(null, FunctionQueueKeys.Wire.Succeeded));

            body.Should().Be("Hello\n");
            response.StatusCode.Should().Be(200);
            response.ContentType.Should().StartWith("text/plain");
            response.Headers["x-blocks-run-id"].ToString().Should().Be("run_1");
            response.Headers.CacheControl.ToString().Should().Be("no-cache");
            abort.Aborted.Should().BeFalse();
        }

        [Fact]
        public async Task A_plain_text_stream_whose_run_failed_is_cut_off_not_ended_cleanly()
        {
            var (body, _, abort) = await WriteAsync(false,
                new FunctionStreamPiece("part"), new FunctionStreamPiece(null, "FAILED", "USER_RUNTIME_ERROR", "boom"));

            body.Should().Be("part");
            abort.Aborted.Should().BeTrue("the client must see an incomplete answer, not a finished one");
        }

        [Fact]
        public async Task Sse_frames_each_piece_and_says_done()
        {
            var (body, response, _) = await WriteAsync(true,
                new FunctionStreamPiece("a\nb"), new FunctionStreamPiece("c"), new FunctionStreamPiece(null, FunctionQueueKeys.Wire.Succeeded));

            response.ContentType.Should().StartWith("text/event-stream");
            body.Should().Be("data: a\ndata: b\n\ndata: c\n\nevent: done\ndata: {}\n\n");
        }

        [Fact]
        public async Task Sse_reports_a_failed_run_as_an_error_event()
        {
            var (body, _, abort) = await WriteAsync(true,
                new FunctionStreamPiece("a"), new FunctionStreamPiece(null, "TIMED_OUT", "TIMED_OUT", "too slow"));

            body.Should().StartWith("data: a\n\n").And.Contain("event: error\ndata: {\"runId\":\"run_1\",\"status\":\"TIMED_OUT\"");
            abort.Aborted.Should().BeFalse();
        }
    }
}
