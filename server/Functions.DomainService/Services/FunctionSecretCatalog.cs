using Blocks.Secrets;
using Functions.DomainService.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// The secret catalog as the function management surface needs it: id → name (export) and name → ids (import,
    /// the "needs secret" warning). <b>Metadata only</b>: no method here reads a value, so nothing here can leak one.
    /// <para>
    /// Runs in the caller's request, under the caller's own <c>BlocksContext</c>, so the SDK applies the caller's
    /// tenant and access rules. Never cached (2026-10-06). Throws when the secret store cannot be read; callers decide
    /// whether that refuses the request (export, import) or only leaves a warning out (detail, list).
    /// </para>
    /// </summary>
    public interface IFunctionSecretCatalog
    {
        /// <summary>The name of every id that exists in this tenant; ids with no secret are left out.</summary>
        Task<IReadOnlyDictionary<string, string>> NamesByIdAsync(
            IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken = default);

        /// <summary>Every id per exact name; every asked name is a key, with an empty list when none matches.</summary>
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> IdsByNameAsync(
            IReadOnlyCollection<string> names, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="IFunctionSecretCatalog"/>
    public sealed class FunctionSecretCatalog : IFunctionSecretCatalog
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public FunctionSecretCatalog(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<IReadOnlyDictionary<string, string>> NamesByIdAsync(
            IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (secretIds.Count == 0) return result;

            // ISecretService is scoped (it reads the request's BlocksContext); this catalog is a singleton.
            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
            foreach (var id in secretIds.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal))
            {
                var secret = await secrets.GetAsync(id, cancellationToken);
                // A deleted secret is gone for a reference: the runner refuses it as SECRET_UNRESOLVED.
                if (secret is null || string.IsNullOrEmpty(secret.Name)
                    || string.Equals(secret.Status, "Deleted", StringComparison.OrdinalIgnoreCase)) continue;
                result[id] = secret.Name;
            }
            return result;
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> IdsByNameAsync(
            IReadOnlyCollection<string> names, CancellationToken cancellationToken = default)
        {
            if (names.Count == 0) return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
            return await FunctionSecretNameLookup.FindExactIdsAsync(secrets, names, cancellationToken);
        }
    }
}
