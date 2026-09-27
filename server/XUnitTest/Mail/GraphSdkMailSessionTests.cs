using System.Net;
using System.Text;
using FluentAssertions;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Office365;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The Graph SDK adapter at the HTTP boundary: the requests it makes, where the token goes and,
    /// just as much, where it does not.
    /// </summary>
    public class GraphSdkMailSessionTests
    {
        private const string Token = "an-access-token";
        private const string UploadUrl = "https://outlook.office.com/api/v2.0/Users('u')/Messages('draft-1')/AttachmentSessions('s')?authtoken=pre-authenticated";

        private readonly RecordingHandler _handler = new();

        private GraphSdkMailSession Session() => new(new HttpClient(_handler), Token);

        private static Message AMessage() => new()
        {
            Subject = "Invoice",
            Body = new ItemBody { ContentType = BodyType.Html, Content = "<p>Invoice</p>" },
            ToRecipients = [new Recipient { EmailAddress = new EmailAddress { Address = "recipient@example.com" } }]
        };

        [Fact]
        public async Task SendMail_PostsToTheMailboxWithTheBearerToken_AndSavesToSentItems()
        {
            _handler.Respond(HttpStatusCode.Accepted);

            await Session().SendMailAsync("mailer@contoso.com", AMessage());

            var request = _handler.Requests.Single();
            request.Method.Should().Be(HttpMethod.Post);
            request.Uri.Should().Be("https://graph.microsoft.com/v1.0/users/mailer%40contoso.com/sendMail");
            request.Authorization.Should().Be($"Bearer {Token}");
            // The SDK writes this action body's keys capitalised; Graph reads them case-insensitively.
            request.Body.Should().Contain("\"SaveToSentItems\":true");
            request.Body.Should().Contain("recipient@example.com");
        }

        [Fact]
        public async Task AGraphError_SurfacesAsAnODataErrorWithItsStatusAndCode()
        {
            _handler.Respond(HttpStatusCode.Forbidden, """{"error":{"code":"ErrorAccessDenied","message":"Access is denied."}}""");

            var act = () => Session().SendMailAsync("mailer@contoso.com", AMessage());

            var error = (await act.Should().ThrowAsync<ODataError>()).Which;
            error.ResponseStatusCode.Should().Be(403);
            error.Error!.Code.Should().Be("ErrorAccessDenied");
            Office365GraphMailSender.Classify(error).Should().Be(Office365FailureCode.PermissionDenied);
        }

        [Fact]
        public async Task AThrottledSend_IsNotRetried()
        {
            _handler.Respond(HttpStatusCode.ServiceUnavailable, """{"error":{"code":"ErrorServerBusy","message":"busy"}}""");

            var act = () => Session().SendMailAsync("mailer@contoso.com", AMessage());

            await act.Should().ThrowAsync<ODataError>();
            _handler.Requests.Should().ContainSingle("no Graph retry middleware sits in front of a send");
        }

        [Fact]
        public async Task TheDraftPath_CreatesAttachesAndSends()
        {
            _handler.Respond(HttpStatusCode.Created, """{"id":"draft-1"}""");
            _handler.Respond(HttpStatusCode.Created, """{"id":"att-1"}""");
            _handler.Respond(HttpStatusCode.Accepted);
            _handler.Respond(HttpStatusCode.NoContent);

            var session = Session();
            var draftId = await session.CreateDraftAsync("mailer@contoso.com", AMessage());
            await session.AddAttachmentAsync("mailer@contoso.com", draftId, new MailAttachment { FileName = "a.pdf", ContentType = "application/pdf", Content = [1, 2, 3] });
            await session.SendDraftAsync("mailer@contoso.com", draftId);
            await session.DeleteDraftAsync("mailer@contoso.com", draftId);

            draftId.Should().Be("draft-1");
            _handler.Requests.Select(r => $"{r.Method} {r.Uri}").Should().Equal(
                "POST https://graph.microsoft.com/v1.0/users/mailer%40contoso.com/messages",
                "POST https://graph.microsoft.com/v1.0/users/mailer%40contoso.com/messages/draft-1/attachments",
                "POST https://graph.microsoft.com/v1.0/users/mailer%40contoso.com/messages/draft-1/send",
                "DELETE https://graph.microsoft.com/v1.0/users/mailer%40contoso.com/messages/draft-1");
            _handler.Requests[1].Body.Should().Contain("\"@odata.type\":\"#microsoft.graph.fileAttachment\"");
            _handler.Requests[1].Body.Should().Contain("\"contentBytes\":\"AQID\"");
        }

        [Fact]
        public async Task AnUploadSession_SendsTheSlicesToTheOutlookHost_WithoutTheToken()
        {
            const int size = 3 * 1024 * 1024 + 1;
            _handler.Respond(HttpStatusCode.OK, $$"""{"uploadUrl":"{{UploadUrl}}","expirationDateTime":"2099-01-01T00:00:00Z","nextExpectedRanges":["0-"]}""");
            _handler.Respond(HttpStatusCode.Created, location: "https://outlook.office.com/api/v2.0/Users('u')/Messages('draft-1')/Attachments('att-1')");

            await Session().UploadAttachmentAsync(
                "mailer@contoso.com",
                "draft-1",
                new MailAttachment { FileName = "big.pdf", ContentType = "application/pdf", Content = new byte[size] });

            _handler.Requests.Should().HaveCount(2);

            var create = _handler.Requests[0];
            create.Uri.Should().Be("https://graph.microsoft.com/v1.0/users/mailer%40contoso.com/messages/draft-1/attachments/createUploadSession");
            create.Authorization.Should().Be($"Bearer {Token}");
            create.Body.Should().Contain($"\"size\":{size}");

            var slice = _handler.Requests[1];
            slice.Method.Should().Be(HttpMethod.Put);
            slice.Uri.Should().StartWith("https://outlook.office.com/");
            slice.Authorization.Should().BeNull("the upload URL is pre-authenticated; the Graph token must not leave Graph");
            slice.ContentRange.Should().Be($"bytes 0-{size - 1}/{size}");
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses = new();

            public List<RecordedRequest> Requests { get; } = [];

            public void Respond(HttpStatusCode status, string? json = null, string? location = null) =>
                _responses.Enqueue(() =>
                {
                    var response = new HttpResponseMessage(status);
                    if (json is not null)
                    {
                        response.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    }
                    if (location is not null)
                    {
                        response.Headers.Location = new Uri(location);
                    }
                    return response;
                });

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);

                Requests.Add(new RecordedRequest(
                    request.Method,
                    request.RequestUri!.AbsoluteUri,
                    request.Headers.Authorization?.ToString(),
                    request.Content?.Headers.ContentRange?.ToString(),
                    body is null || body.Length > 64 * 1024 ? null : Encoding.UTF8.GetString(body)));

                return _responses.Count > 0
                    ? _responses.Dequeue()()
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
        }

        private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string? ContentRange, string? Body);
    }
}
