using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// One page of the inbox delta: the messages on it, and exactly one of the two links.
    /// </summary>
    /// <param name="Messages">Messages added or changed since the cursor. Removals are left out.</param>
    /// <param name="NextLink">More pages follow; resume from here.</param>
    /// <param name="DeltaLink">The round is complete; the next poll starts from here.</param>
    public sealed record Office365InboxPage(
        IReadOnlyList<Office365InboxMessage> Messages,
        string? NextLink,
        string? DeltaLink);

    /// <param name="Id">The Graph message id, for fetching the MIME.</param>
    /// <param name="InternetMessageId">The RFC 5322 <c>Message-ID</c>, angle brackets included.</param>
    public sealed record Office365InboxMessage(string Id, string? InternetMessageId);

    /// <summary>
    /// The seam over the Graph inbox reads, so the poller can be tested without a network.
    /// </summary>
    /// <remarks>
    /// One member per Graph request and no decisions, as with <see cref="IOffice365GraphMailSession"/>:
    /// where a round starts, what a stale cursor means and which messages are fetched all live in
    /// the poller.
    /// </remarks>
    public interface IOffice365GraphInboxSession
    {
        /// <summary>
        /// <c>GET /users/{mailbox}/mailFolders/inbox/messages/delta</c> when <paramref name="cursor"/>
        /// is null; otherwise the next or delta link a previous page returned.
        /// </summary>
        Task<Office365InboxPage> GetInboxDeltaPageAsync(string mailbox, string? cursor, CancellationToken cancellationToken = default);

        /// <summary><c>GET /users/{mailbox}/messages/{id}/$value</c>. The message as MIME.</summary>
        Task<Stream> GetMimeAsync(string mailbox, string messageId, CancellationToken cancellationToken = default);
    }

    /// <summary>Opens an inbox session for an already-acquired Graph token.</summary>
    public interface IOffice365GraphInboxSessionFactory
    {
        IOffice365GraphInboxSession Create(string accessToken);
    }

    public sealed class Office365GraphInboxSessionFactory : IOffice365GraphInboxSessionFactory
    {
        /// <summary>The named <see cref="HttpClient"/> inbox reads go through.</summary>
        public const string HttpClientName = "Office365GraphInbox";

        private readonly IHttpClientFactory _httpClientFactory;

        public Office365GraphInboxSessionFactory(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public IOffice365GraphInboxSession Create(string accessToken) =>
            new GraphSdkInboxSession(_httpClientFactory.CreateClient(HttpClientName), accessToken);
    }

    /// <summary>
    /// The Graph SDK behind <see cref="IOffice365GraphInboxSession"/>.
    /// </summary>
    /// <remarks>
    /// No retry middleware here either. A read is safe to repeat, but the poller already repeats
    /// it: a failed round keeps its cursor, and the next tick resumes from it.
    /// </remarks>
    internal sealed class GraphSdkInboxSession : IOffice365GraphInboxSession
    {
        /// <summary>
        /// Fifty messages per delta page. Only ids come back, so a page is small; the MIME for each
        /// new message is a request of its own.
        /// </summary>
        private const string PreferPageSize = "odata.maxpagesize=50";

        private readonly GraphServiceClient _client;

        public GraphSdkInboxSession(HttpClient httpClient, string accessToken)
        {
            _client = new GraphServiceClient(
                httpClient,
                new BaseBearerTokenAuthenticationProvider(new StaticAccessTokenProvider(accessToken)));
        }

        public async Task<Office365InboxPage> GetInboxDeltaPageAsync(string mailbox, string? cursor, CancellationToken cancellationToken = default)
        {
            var delta = _client.Users[mailbox].MailFolders["inbox"].Messages.Delta;

            // A next or delta link already carries the query the round started with, $select
            // included; only the page-size preference has to be sent again.
            var response = cursor is null
                ? await delta.GetAsDeltaGetResponseAsync(
                    request =>
                    {
                        request.QueryParameters.Select = ["internetMessageId"];
                        request.Headers.Add("Prefer", PreferPageSize);
                    },
                    cancellationToken).ConfigureAwait(false)
                : await delta.WithUrl(cursor).GetAsDeltaGetResponseAsync(
                    request => request.Headers.Add("Prefer", PreferPageSize),
                    cancellationToken).ConfigureAwait(false);

            if (response is null)
            {
                throw new InvalidOperationException("Graph returned an empty delta page.");
            }

            var messages = (response.Value ?? [])
                .Where(message => message.Id is not null && message.AdditionalData?.ContainsKey("@removed") != true)
                .Select(message => new Office365InboxMessage(message.Id!, message.InternetMessageId))
                .ToList();

            return new Office365InboxPage(messages, response.OdataNextLink, response.OdataDeltaLink);
        }

        public async Task<Stream> GetMimeAsync(string mailbox, string messageId, CancellationToken cancellationToken = default) =>
            await _client.Users[mailbox].Messages[messageId].Content
                .GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Graph returned no MIME content.");
    }
}
