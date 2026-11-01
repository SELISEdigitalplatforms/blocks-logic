using Blocks.Secrets;
using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DomainService.Notification.Push
{
    /// <summary>
    /// Reads a push secret (the service-account JSON or the web config) from Blocks Secrets. Read only: blocks-os
    /// writes them. Values are never logged and never returned to a client except the web config to signed-in apps.
    /// </summary>
    public interface IPushCredentialStore
    {
        /// <summary>
        /// The value of the secret <paramref name="secretId"/> when set, else of the secret named exactly
        /// <paramref name="secretName"/> (records written before ids were stored). Null when there is none.
        /// Runs in its own tenant context (workers have no authenticated user). A store failure throws.
        /// </summary>
        Task<string?> ReadAsync(string tenantId, string? secretId, string secretName, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushCredentialStore"/>
    public sealed class PushCredentialStore : IPushCredentialStore
    {
        private const int SearchPageSize = 50;

        private readonly IServiceScopeFactory _scopeFactory;

        public PushCredentialStore(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public Task<string?> ReadAsync(string tenantId, string? secretId, string secretName, CancellationToken cancellationToken) =>
            TenantSecretReader.RunAsync(_scopeFactory, tenantId, async secrets =>
            {
                var id = string.IsNullOrWhiteSpace(secretId) ? await FindIdAsync(secrets, secretName, cancellationToken) : secretId;
                return id is null ? null : await secrets.GetValueAsync(id, cancellationToken);
            });

        /// <summary>The id of the secret named exactly <paramref name="secretName"/> (search is a contains match).</summary>
        private static async Task<string?> FindIdAsync(ISecretService secrets, string secretName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(secretName))
                return null;

            var page = await secrets.FindAsync(new SecretFilter { Search = secretName, PageSize = SearchPageSize, PageNumber = 0 }, cancellationToken);
            return page.Data?.FirstOrDefault(s => string.Equals(s.Name, secretName, StringComparison.Ordinal))?.SecretId;
        }
    }
}
