using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Mail.DomainService.Shared.Utilities
{
    /// <summary>
    /// Reads one Blocks Secrets value inside an authenticated context for a given tenant, then
    /// restores the caller's context. The one variant of this pattern in the service: the Office 365
    /// token provider, the mail password resolver and the service bus connection string resolver
    /// all read through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opens its own DI scope rather than taking <c>ISecretService</c> as a dependency: the callers
    /// are singletons, and <c>ISecretService</c> is scoped because it reads the request-scoped
    /// <see cref="BlocksContext"/>. Injecting it directly would either fail scope validation or
    /// capture the first caller's tenant forever.
    /// </para>
    /// <para>
    /// Blocks Secrets rejects any context with <c>!IsAuthenticated</c> or a blank tenant id, and the
    /// queue consumers that drive most reads carry no authenticated identity of its own. The
    /// synthetic context below lives for exactly one read and is restored afterwards, also when the
    /// read throws.
    /// </para>
    /// </remarks>
    public static class TenantSecretReader
    {
        public static async Task<string> ReadAsync(
            IServiceScopeFactory scopeFactory,
            string tenantId,
            string secretReference,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);

            var restore = BlocksContext.GetContext();

            try
            {
                EnterTenantContext(tenantId, restore);

                using var scope = scopeFactory.CreateScope();
                var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

                return await secrets.GetValueAsync(secretReference, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                BlocksContext.SetContext(restore, restore is not null);
            }
        }

        /// <summary>
        /// Puts an authenticated, tenant-scoped context in place for the duration of one read.
        /// </summary>
        /// <remarks>
        /// <c>isAuthenticated</c> MUST be true: Blocks.Secrets rejects any context with
        /// <c>!IsAuthenticated</c> or a blank tenant id before every value read. The context is
        /// scoped to the record's own tenant rather than to whoever triggered the read, so a send
        /// queued by one tenant can never read another tenant's secret.
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
                // scoped to the record's tenant and never the caller's.
                true);
        }
    }
}
