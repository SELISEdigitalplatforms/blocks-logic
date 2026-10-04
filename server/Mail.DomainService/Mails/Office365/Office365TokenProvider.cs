using Blocks.Genesis;
using Blocks.Secrets;
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

        private async Task<string> ResolveSecretAsync(Office365TokenRequest request, CancellationToken cancellationToken)
        {
            var restore = BlocksContext.GetContext();

            try
            {
                EnterTenantContext(request.BlocksTenantId, restore);

                using var scope = _scopeFactory.CreateScope();
                var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

                return await secrets.GetValueAsync(request.ClientSecretReference, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                BlocksContext.SetContext(restore, restore is not null);
            }
        }

        /// <summary>
        /// Puts an authenticated, tenant-scoped context in place for the duration of one resolve.
        /// </summary>
        /// <remarks>
        /// <c>isAuthenticated</c> MUST be true: Blocks.Secrets rejects any context with
        /// <c>!IsAuthenticated</c> or a blank tenant id before every value read. The context is
        /// scoped to the configuration's own tenant rather than to whoever triggered the send, so
        /// a send queued by one tenant can never read another tenant's secret.
        /// </remarks>
        private static void EnterTenantContext(string tenantId, BlocksContext? current)
        {
            BlocksContext.SetContext(
                BlocksContext.Create(
                    tenantId: tenantId,
                    roles: [],
                    userId: current?.UserId ?? string.Empty,
                    isAuthenticated: true,
                    requestUri: string.Empty,
                    organizationId: current?.OrganizationId ?? string.Empty,
                    expireOn: DateTime.MinValue,
                    email: string.Empty,
                    permissions: [],
                    userName: current?.UserName ?? string.Empty,
                    phoneNumber: string.Empty,
                    displayName: current?.UserName ?? string.Empty,
                    oauthToken: string.Empty,
                    originalTenantId: current?.OriginalTenantId ?? tenantId,
                    applicationDomain: string.Empty),
                // changeContext: true so GetContext() returns this async-local context even where an
                // ambient HttpContext still carries an authenticated identity, keeping the read
                // scoped to the configuration's tenant and never the caller's.
                true);
        }
    }
}
