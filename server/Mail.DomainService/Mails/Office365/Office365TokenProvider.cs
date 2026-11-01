using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// Resolves the client secret in its tenant and exchanges it for an Exchange Online token.
    /// </summary>
    /// <remarks>
    /// The secret is read through <see cref="TenantSecretReader"/>, which opens a fresh DI scope for
    /// the scoped <c>ISecretService</c>, enters an authenticated context for the request's tenant
    /// for exactly one read and restores the caller's context afterwards. See its remarks for why.
    /// </remarks>
    public class Office365TokenProvider : IOffice365TokenProvider
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOffice365TokenAcquirer _acquirer;

        public Office365TokenProvider(IServiceScopeFactory scopeFactory, IOffice365TokenAcquirer acquirer)
        {
            _scopeFactory = scopeFactory;
            _acquirer = acquirer;
        }

        public virtual async Task<string> GetTokenAsync(
            Office365TokenRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var token = await AcquireAsync(request, cancellationToken).ConfigureAwait(false);
            return token.Token;
        }

        /// <summary>
        /// One secret resolution followed by one token request. The unit a cache would wrap.
        /// </summary>
        protected async Task<Office365AccessToken> AcquireAsync(
            Office365TokenRequest request,
            CancellationToken cancellationToken)
        {
            var secret = await ResolveSecretAsync(request, cancellationToken).ConfigureAwait(false);

            return await _acquirer.AcquireAsync(request, secret, cancellationToken).ConfigureAwait(false);
        }

        // The authenticated, tenant-scoped read and the restore of the caller's context live in
        // TenantSecretReader, shared with the mail password and service bus resolvers.
        private Task<string> ResolveSecretAsync(Office365TokenRequest request, CancellationToken cancellationToken) =>
            TenantSecretReader.ReadAsync(_scopeFactory, request.BlocksTenantId, request.ClientSecretReference, cancellationToken);
    }
}
