using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.Messages.Item.Attachments.CreateUploadSession;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// The seam over the Graph mail calls, so the sender can be tested without a network.
    /// </summary>
    /// <remarks>
    /// One member per Graph request and no decisions: which path a message takes, what happens to a
    /// half-built draft and how a failure is named all live in <see cref="Office365GraphMailSender"/>,
    /// where the tests can reach them.
    /// </remarks>
    public interface IOffice365GraphMailSession
    {
        /// <summary><c>POST /users/{mailbox}/sendMail</c>. One request, attachments inline.</summary>
        Task SendMailAsync(string mailbox, Message message, CancellationToken cancellationToken = default);

        /// <summary><c>POST /users/{mailbox}/messages</c>. Returns the draft's id.</summary>
        Task<string> CreateDraftAsync(string mailbox, Message message, CancellationToken cancellationToken = default);

        /// <summary><c>POST /users/{mailbox}/messages/{id}/attachments</c>, for an attachment under 3 MB.</summary>
        Task AddAttachmentAsync(string mailbox, string draftId, MailAttachment attachment, CancellationToken cancellationToken = default);

        /// <summary>
        /// <c>createUploadSession</c> and the slices after it, for an attachment of 3 MB or more.
        /// </summary>
        Task UploadAttachmentAsync(string mailbox, string draftId, MailAttachment attachment, CancellationToken cancellationToken = default);

        /// <summary><c>POST /users/{mailbox}/messages/{id}/send</c>.</summary>
        Task SendDraftAsync(string mailbox, string draftId, CancellationToken cancellationToken = default);

        /// <summary><c>DELETE /users/{mailbox}/messages/{id}</c>.</summary>
        Task DeleteDraftAsync(string mailbox, string draftId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The Graph SDK behind <see cref="IOffice365GraphMailSession"/>. The whole of the Graph surface
    /// in this service.
    /// </summary>
    /// <remarks>
    /// The <see cref="HttpClient"/> comes from the factory and carries none of the Graph SDK's
    /// default middleware, deliberately: its retry handler resends on 429, 503 and 504, and a
    /// <c>sendMail</c> that timed out at a gateway may already have been accepted. A second attempt
    /// is a second copy in someone's inbox. Nothing here retries a send.
    /// </remarks>
    internal sealed class GraphSdkMailSession : IOffice365GraphMailSession
    {
        /// <summary>
        /// Upload slice size. Graph requires slices in multiples of 320 KiB and each request under
        /// 4 MB; ten multiples is the largest size that satisfies both.
        /// </summary>
        private const int UploadSliceBytes = 320 * 1024 * 10;

        private readonly GraphServiceClient _client;

        public GraphSdkMailSession(HttpClient httpClient, string accessToken)
        {
            _client = new GraphServiceClient(
                httpClient,
                new BaseBearerTokenAuthenticationProvider(new StaticAccessTokenProvider(accessToken)));
        }

        public Task SendMailAsync(string mailbox, Message message, CancellationToken cancellationToken = default) =>
            _client.Users[mailbox].SendMail.PostAsync(
                new SendMailPostRequestBody { Message = message, SaveToSentItems = true },
                cancellationToken: cancellationToken);

        public async Task<string> CreateDraftAsync(string mailbox, Message message, CancellationToken cancellationToken = default)
        {
            var draft = await _client.Users[mailbox].Messages
                .PostAsync(message, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return draft?.Id ?? throw new InvalidOperationException("Graph created a draft without an id.");
        }

        public Task AddAttachmentAsync(string mailbox, string draftId, MailAttachment attachment, CancellationToken cancellationToken = default) =>
            _client.Users[mailbox].Messages[draftId].Attachments.PostAsync(
                new FileAttachment
                {
                    Name = attachment.FileName,
                    ContentType = attachment.ContentType,
                    ContentBytes = attachment.Content
                },
                cancellationToken: cancellationToken);

        public async Task UploadAttachmentAsync(string mailbox, string draftId, MailAttachment attachment, CancellationToken cancellationToken = default)
        {
            var session = await _client.Users[mailbox].Messages[draftId].Attachments.CreateUploadSession
                .PostAsync(
                    new CreateUploadSessionPostRequestBody
                    {
                        AttachmentItem = new AttachmentItem
                        {
                            AttachmentType = AttachmentType.File,
                            Name = attachment.FileName,
                            ContentType = attachment.ContentType,
                            Size = attachment.Content.LongLength
                        }
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Graph returned no upload session.");

            using var content = new MemoryStream(attachment.Content, writable: false);

            // The upload URL is pre-authenticated and on an Outlook host, not Graph. The token
            // provider's host allow-list is what keeps the bearer token off those requests.
            var upload = new LargeFileUploadTask<FileAttachment>(session, content, UploadSliceBytes, _client.RequestAdapter);
            var result = await upload.UploadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!result.UploadSucceeded)
            {
                throw new InvalidOperationException("Graph did not complete the attachment upload.");
            }
        }

        public Task SendDraftAsync(string mailbox, string draftId, CancellationToken cancellationToken = default) =>
            _client.Users[mailbox].Messages[draftId].Send.PostAsync(cancellationToken: cancellationToken);

        public Task DeleteDraftAsync(string mailbox, string draftId, CancellationToken cancellationToken = default) =>
            _client.Users[mailbox].Messages[draftId].DeleteAsync(cancellationToken: cancellationToken);

        /// <summary>
        /// Hands the already-acquired token to Kiota, for Graph only.
        /// </summary>
        /// <remarks>
        /// The token is bought and cached by <see cref="IOffice365TokenProvider"/>; this only
        /// presents it. Any other host — an upload session's Outlook URL above all — gets no token.
        /// </remarks>
        private sealed class StaticAccessTokenProvider(string accessToken) : IAccessTokenProvider
        {
            public AllowedHostsValidator AllowedHostsValidator { get; } = new([Office365GraphMailSender.GraphHost]);

            public Task<string> GetAuthorizationTokenAsync(
                Uri uri,
                Dictionary<string, object>? additionalAuthenticationContext = null,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(AllowedHostsValidator.IsUrlHostValid(uri) ? accessToken : string.Empty);
        }
    }
}
