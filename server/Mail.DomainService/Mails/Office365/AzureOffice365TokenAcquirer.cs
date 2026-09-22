using Azure.Core;
using Azure.Identity;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// Client-credentials token request against the public Microsoft cloud.
    /// </summary>
    /// <remarks>
    /// Deliberately the whole of the Azure Identity surface in this service. Everything above it
    /// works against <see cref="IOffice365TokenAcquirer"/>, so caching, coalescing and failure
    /// classification are all testable without a network or a directory.
    /// </remarks>
    public sealed class AzureOffice365TokenAcquirer : IOffice365TokenAcquirer
    {
        /// <summary>
        /// The Exchange Online SMTP scope. Exactly this value: a token for Graph or for a
        /// resource-specific scope is accepted by Entra and then refused by SMTP, which surfaces
        /// as an authentication failure with nothing pointing at the scope.
        /// </summary>
        public const string ExchangeOnlineScope = "https://outlook.office365.com/.default";

        public async Task<Office365AccessToken> AcquireAsync(
            Office365TokenRequest request,
            string clientSecret,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var credential = new ClientSecretCredential(request.EntraTenantId, request.ClientId, clientSecret);

            var token = await credential
                .GetTokenAsync(new TokenRequestContext([ExchangeOnlineScope]), cancellationToken)
                .ConfigureAwait(false);

            return new Office365AccessToken(token.Token, token.ExpiresOn);
        }
    }
}
