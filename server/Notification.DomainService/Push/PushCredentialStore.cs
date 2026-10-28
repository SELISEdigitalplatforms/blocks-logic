using Blocks.Secrets;
using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DomainService.Notification.Push
{
    /// <summary>The Firebase service-account JSON in Blocks Secrets, found by secret name. Never logged, never returned to a client.</summary>
    public interface IPushCredentialStore
    {
        /// <summary>The JSON of <paramref name="secretName"/> in <paramref name="tenantId"/>; null when there is none.
        /// Runs in its own tenant context (workers have no authenticated user).</summary>
        Task<string?> ReadAsync(string tenantId, string secretName, CancellationToken cancellationToken);

        /// <summary>Stores <paramref name="json"/> under <paramref name="secretName"/> for the caller's tenant: rotates the
        /// existing secret of that name, or creates a <c>service</c> secret.</summary>
        Task WriteAsync(string secretName, string json, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushCredentialStore"/>
    public sealed class PushCredentialStore : IPushCredentialStore
    {
        private const string Tag = "notifications";
        private const int SearchPageSize = 50;

        private readonly IServiceScopeFactory _scopeFactory;

        public PushCredentialStore(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public Task<string?> ReadAsync(string tenantId, string secretName, CancellationToken cancellationToken) =>
            TenantSecretReader.RunAsync(_scopeFactory, tenantId, async secrets =>
            {
                var secretId = await FindIdAsync(secrets, secretName, cancellationToken);
                return secretId is null ? null : await secrets.GetValueAsync(secretId, cancellationToken);
            });

        public async Task WriteAsync(string secretName, string json, CancellationToken cancellationToken)
        {
            // The caller's own (request) context: Blocks Secrets checks it, as for any secret write from the console.
            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

            var secretId = await FindIdAsync(secrets, secretName, cancellationToken);
            if (secretId is null)
            {
                await secrets.SetAsync(new SetSecretRequest
                {
                    Name = secretName,
                    Value = json,
                    Type = SecretTypes.Service,
                    Tags = [Tag],
                }, cancellationToken);
                return;
            }

            await secrets.RotateAsync(secretId, new RotateSecretRequest { Value = json }, cancellationToken);
        }

        /// <summary>The id of the secret named exactly <paramref name="secretName"/> (search is a contains match).</summary>
        private static async Task<string?> FindIdAsync(ISecretService secrets, string secretName, CancellationToken cancellationToken)
        {
            var page = await secrets.FindAsync(new SecretFilter { Search = secretName, PageSize = SearchPageSize, PageNumber = 0 }, cancellationToken);
            return page.Data?.FirstOrDefault(s => string.Equals(s.Name, secretName, StringComparison.Ordinal))?.SecretId;
        }
    }
}
