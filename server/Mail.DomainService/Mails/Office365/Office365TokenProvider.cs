using Blocks.Genesis;
using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// Resolves the client secret in its tenant and exchanges it for an Exchange Online token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opens its own DI scope rather than taking <c>ISecretService</c> as a dependency: the mail
    /// senders are registered as singletons in the worker and transients in the API, and
    /// <c>ISecretService</c> is scoped because it reads the request-scoped
    /// <see cref="BlocksContext"/>. Injecting it directly would either fail scope validation or
    /// capture the first caller's tenant forever.
    /// </para>
    /// <para>
    /// It also establishes the context that read needs. Blocks Secrets rejects any context with
    /// <c>!IsAuthenticated</c> or a blank tenant id, and the queue consumer that drives most sends
    /// carries no authenticated identity of its own — a tenant id alone is not enough. The
    /// synthetic context below lives for exactly one resolve and is restored afterwards. This
    /// mirrors what <c>ProxyVariableResolver</c> already does for proxy configuration variables,
    /// which is the existing precedent for reading a secret from inside the worker.
    /// </para>
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
