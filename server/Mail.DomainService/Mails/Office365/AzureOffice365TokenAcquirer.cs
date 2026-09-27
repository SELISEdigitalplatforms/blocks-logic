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
        public async Task<Office365AccessToken> AcquireAsync(
            Office365TokenRequest request,
            string clientSecret,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var credential = new ClientSecretCredential(request.EntraTenantId, request.ClientId, clientSecret);

            var token = await credential
                .GetTokenAsync(new TokenRequestContext([request.Scope]), cancellationToken)
                .ConfigureAwait(false);

            return new Office365AccessToken(token.Token, token.ExpiresOn);
        }
    }
}
