namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// Everything needed to obtain one Exchange Online token.
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
    public sealed record Office365TokenRequest(
        string BlocksTenantId,
        string EntraTenantId,
        string ClientId,
        string ClientSecretReference);

    /// <summary>Obtains an Exchange Online access token for an Office 365 mail configuration.</summary>
    public interface IOffice365TokenProvider
    {
        /// <summary>
        /// Returns an access token for <c>https://outlook.office365.com/.default</c>.
        /// </summary>
        /// <remarks>
        /// The returned string is a credential. It goes straight to the SMTP authentication call
        /// and is never logged, cached on a message, or attached to an event.
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
