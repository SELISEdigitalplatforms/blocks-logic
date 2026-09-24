namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// The resources an Office 365 token can be bought for.
    /// </summary>
    /// <remarks>
    /// Exactly these values. Entra issues a token for any scope the application asks for, and the
    /// resource then refuses one issued for a different audience with an authentication failure
    /// that points nowhere near the scope — so the caller names the resource it is about to call,
    /// and nothing guesses.
    /// </remarks>
    public static class Office365TokenScopes
    {
        /// <summary>
        /// Exchange Online, for IMAP or SMTP with SASL XOAUTH2. Nothing requests it today: both
        /// directions of Office 365 go through Graph.
        /// </summary>
        public const string ExchangeOnline = "https://outlook.office365.com/.default";

        /// <summary>Microsoft Graph, for outbound <c>sendMail</c> and the inbound delta read.</summary>
        public const string Graph = "https://graph.microsoft.com/.default";
    }

    /// <summary>
    /// Everything needed to obtain one Office 365 token.
    /// </summary>
    /// <param name="BlocksTenantId">
    /// The ambient Blocks tenant. Decides which tenant's secret store is read, and is never taken
    /// from the mail configuration — <paramref name="EntraTenantId"/> is a different identity
    /// living in a caller-supplied field.
    /// </param>
    /// <param name="EntraTenantId">The Microsoft Entra directory the application belongs to.</param>
    /// <param name="ClientId">The Entra application id.</param>
    /// <param name="ClientSecretReference">
    /// The Blocks Secrets id of the client secret. An opaque reference; the plaintext is resolved
    /// at acquisition time and never travels in this record.
    /// </param>
    /// <param name="Scope">One of <see cref="Office365TokenScopes"/>.</param>
    public sealed record Office365TokenRequest(
        string BlocksTenantId,
        string EntraTenantId,
        string ClientId,
        string ClientSecretReference,
        string Scope);

    /// <summary>Obtains an access token for an Office 365 mail configuration.</summary>
    public interface IOffice365TokenProvider
    {
        /// <summary>
        /// Returns an access token for the request's <see cref="Office365TokenRequest.Scope"/>.
        /// </summary>
        /// <remarks>
        /// The returned string is a credential. It goes straight to the IMAP authentication call
        /// or the Graph request and is never logged, cached on a message, or attached to an event.
        /// </remarks>
        Task<string> GetTokenAsync(Office365TokenRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Requests a token from Microsoft. The seam that keeps Azure Identity out of the tests.
    /// </summary>
    public interface IOffice365TokenAcquirer
    {
        Task<Office365AccessToken> AcquireAsync(
            Office365TokenRequest request,
            string clientSecret,
            CancellationToken cancellationToken = default);
    }

    /// <param name="Token">The bearer value. Memory only.</param>
    /// <param name="ExpiresOnUtc">When Microsoft says it stops being accepted.</param>
    public sealed record Office365AccessToken(string Token, DateTimeOffset ExpiresOnUtc);
}
