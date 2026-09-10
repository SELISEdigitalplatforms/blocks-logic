using Blocks.Secrets;

namespace Functions.DomainService.Services
{
    /// <summary>One entry in the picker: enough to display and to reference, never a value.</summary>
    public sealed record SecretCatalogEntry(string Id, string Name);

    public interface IFunctionSecretCatalogService
    {
        Task<IReadOnlyList<SecretCatalogEntry>> ListAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The name-only catalog behind the output-action secret picker (DECISIONS D6).
    /// <para>
    /// Metadata only, and unconditionally through the in-process SDK regardless of which
    /// <see cref="ISecretResolver"/> is configured for reading values: listing secret names
    /// only touches the Mongo-backed catalog, not Key Vault, so it works the same whether or
    /// not Key Vault is reachable. Only <i>value</i> resolution differs by configuration.
    /// </para>
    /// </summary>
    public class FunctionSecretCatalogService : IFunctionSecretCatalogService
    {
        private const int PageSize = 200;

        private readonly ISecretService _secretService;

        public FunctionSecretCatalogService(ISecretService secretService)
        {
            _secretService = secretService;
        }

        public async Task<IReadOnlyList<SecretCatalogEntry>> ListAsync(CancellationToken cancellationToken = default)
        {
            // Two queries, not one. SecretTypes are string constants ("service", "api", "both")
            // and SecretFilter.Type is a single value, so filtering on "service" alone hides
            // every secret the tenant marked "both" — which is explicitly usable as a service
            // secret and therefore belongs in this picker. Merging by id keeps the result
            // correct whether or not the store already folds "both" into a "service" query.
            var entries = new Dictionary<string, SecretCatalogEntry>(StringComparer.Ordinal);

            foreach (var type in new[] { SecretTypes.Service, SecretTypes.Both })
            {
                var page = await _secretService.FindAsync(
                    new SecretFilter { Type = type, PageSize = PageSize },
                    cancellationToken);

                foreach (var secret in page.Data)
                {
                    entries[secret.SecretId] = new SecretCatalogEntry(secret.SecretId, secret.Name);
                }
            }

            return entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
