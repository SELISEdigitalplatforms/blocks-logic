using Blocks.Secrets;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// Exact, case-sensitive "which secret ids have this name" over the Secrets SDK — metadata only, never a value.
    /// <para>
    /// The SDK only searches "name or description contains, any case", one page at a time (blocks-os
    /// <c>SecretFilter.Search</c>), so every page is read and the rows are filtered to the exact name. Deleted secrets
    /// are left out by the SDK itself. Functions' own copy (2026-10-07: no shared code with Proxy). Nothing here is
    /// cached — not the ids, not a miss (2026-10-06: never cache secrets).
    /// </para>
    /// </summary>
    public static class FunctionSecretNameLookup
    {
        internal const int PageSize = 50;

        /// <summary>A safety stop: 100 pages × 50 = 5 000 rows matching the search text.</summary>
        internal const int MaxPages = 100;

        /// <summary>Every id whose name is exactly <paramref name="name"/>. Empty when there is none.</summary>
        public static async Task<IReadOnlyList<string>> FindExactIdsAsync(
            ISecretService secrets, string name, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(secrets);
            var ids = new List<string>();
            if (string.IsNullOrEmpty(name)) return ids;

            for (var page = 1; page <= MaxPages; page++)
            {
                var found = await secrets.FindAsync(
                    new SecretFilter { Search = name, PageSize = PageSize, PageNumber = page }, cancellationToken);
                var data = found?.Data ?? [];

                ids.AddRange(data
                    .Where(s => string.Equals(s.Name, name, StringComparison.Ordinal) && !string.IsNullOrEmpty(s.SecretId))
                    .Select(s => s.SecretId));

                if (data.Count < PageSize || (long)page * PageSize >= (found?.TotalCount ?? 0)) break;
            }

            return ids.Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary><see cref="FindExactIdsAsync"/> for several names; every asked name is a key (empty list = none).</summary>
        public static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FindExactIdsAsync(
            ISecretService secrets, IEnumerable<string> names, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var name in names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal))
            {
                result[name] = await FindExactIdsAsync(secrets, name, cancellationToken);
            }
            return result;
        }
    }
}
