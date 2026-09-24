using System.Net;
using System.Text;
using FluentAssertions;
using Mail.DomainService.Mails.Office365;
using Microsoft.Graph.Models.ODataErrors;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The Graph inbox adapter at the HTTP boundary: the requests it makes and how it reads a page.
    /// </summary>
    public class GraphSdkInboxSessionTests
    {
        private const string Token = "an-access-token";
        private const string DeltaLink = "https://graph.microsoft.com/v1.0/users/support%40contoso.com/mailFolders/inbox/messages/delta?%24deltatoken=round-1";

        private readonly RecordingHandler _handler = new();

        private GraphSdkInboxSession Session() => new(new HttpClient(_handler), Token);

        [Fact]
        public async Task AFreshRound_ListsTheInboxDelta_SelectingOnlyTheMessageId()
        {
            _handler.Respond(HttpStatusCode.OK, """{"value":[],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/delta-1"}""");

            await Session().GetInboxDeltaPageAsync("support@contoso.com", cursor: null);

            var request = _handler.Requests.Single();
            request.Method.Should().Be(HttpMethod.Get);
            request.Uri.Should().StartWith("https://graph.microsoft.com/v1.0/users/support%40contoso.com/mailFolders/inbox/messages/delta");
            request.Uri.Should().Contain("internetMessageId");
            request.Authorization.Should().Be($"Bearer {Token}");
            request.Prefer.Should().Be("odata.maxpagesize=50");
        }

        [Fact]
        public async Task AResumedRound_RequestsTheCursorAsIs()
        {
            _handler.Respond(HttpStatusCode.OK, """{"value":[],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/delta-2"}""");

            await Session().GetInboxDeltaPageAsync("support@contoso.com", DeltaLink);

            var request = _handler.Requests.Single();
            request.Uri.Should().Be(DeltaLink);
            request.Authorization.Should().Be($"Bearer {Token}");
            request.Prefer.Should().Be("odata.maxpagesize=50");
        }

        [Fact]
        public async Task APage_CarriesItsMessagesAndLinks_AndLeavesOutRemovals()
        {
            _handler.Respond(HttpStatusCode.OK, """
                {
                  "value": [
                    { "id": "m1", "internetMessageId": "<m1@contoso.com>" },
                    { "id": "m2", "@removed": { "reason": "deleted" } }
                  ],
                  "@odata.nextLink": "https://graph.microsoft.com/v1.0/next-1"
                }
                """);

            var page = await Session().GetInboxDeltaPageAsync("support@contoso.com", cursor: null);

            page.Messages.Should().Equal(new Office365InboxMessage("m1", "<m1@contoso.com>"));
            page.NextLink.Should().Be("https://graph.microsoft.com/v1.0/next-1");
            page.DeltaLink.Should().BeNull();
        }

        [Fact]
        public async Task TheMime_IsReadFromTheMessageValue()
        {
            _handler.Respond(HttpStatusCode.OK, "Message-ID: <m1@contoso.com>\r\nSubject: Hi\r\n\r\nHello\r\n", "message/rfc822");

            await using var stream = await Session().GetMimeAsync("support@contoso.com", "m1");
            using var reader = new StreamReader(stream);

            (await reader.ReadToEndAsync()).Should().Contain("Subject: Hi");
            _handler.Requests.Single().Uri.Should().Be("https://graph.microsoft.com/v1.0/users/support%40contoso.com/messages/m1/$value");
        }

        [Fact]
        public async Task AnExpiredCursor_SurfacesAsAnODataErrorWith410()
        {
            _handler.Respond(HttpStatusCode.Gone, """{"error":{"code":"SyncStateNotFound","message":"gone"}}""");

            var act = () => Session().GetInboxDeltaPageAsync("support@contoso.com", DeltaLink);

            (await act.Should().ThrowAsync<ODataError>()).Which.ResponseStatusCode.Should().Be(410);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses = new();

            public List<RecordedRequest> Requests { get; } = [];

            public void Respond(HttpStatusCode status, string body, string mediaType = "application/json") =>
                _responses.Enqueue(() => new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, mediaType)
                });

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(new RecordedRequest(
                    request.Method,
                    request.RequestUri!.AbsoluteUri,
                    request.Headers.Authorization?.ToString(),
                    request.Headers.TryGetValues("Prefer", out var prefer) ? string.Join(",", prefer) : null));

                return Task.FromResult(_responses.Count > 0
                    ? _responses.Dequeue()()
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
        }

        private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string? Prefer);
    }
}
