using Microsoft.Extensions.Logging;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;
using Blocks.Genesis.Entities.Logic;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Fills <see cref="ProxyDetailEntity.SecretIds"/> on save and revert (PX-9, 2026-10-07): the token keeps the
    /// name, the row keeps name + id, and a call reads by id once. Same idea as Functions, which store the id.
    /// </summary>
    internal static class ProxySecretIdBinder
    {
        /// <summary>
        /// Looks up the id of every <c>{{$VAR.name}}</c> in <paramref name="proxy"/>. <c>Missing</c> lists names
        /// that do not exist in Blocks Secrets. When the secret store cannot be read, nothing is reported missing
        /// (a save must not fail because Key Vault blinked): known ids are kept and any other name falls back to a
        /// name lookup at call time.
        /// </summary>
        public static async Task<(Dictionary<string, string> Ids, IReadOnlyList<string> Missing)> BindAsync(
            IProxyVariableResolver? resolver, string tenantId, ProxyDetailEntity proxy, ILogger logger,
            CancellationToken ct = default)
        {
            var names = ProxyVarRef.NamesIn(proxy);
            var kept = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (proxy.SecretIds.TryGetValue(name, out var id) && !string.IsNullOrEmpty(id)) kept[name] = id;
            }

            if (names.Count == 0 || resolver is null)
            {
                return (kept, Array.Empty<string>());
            }

            try
            {
                var found = await resolver.LookupIdsAsync(names, tenantId, ct);
                var ids = new Dictionary<string, string>(found, StringComparer.Ordinal);
                var missing = names.Where(n => !ids.ContainsKey(n)).ToList();
                return (ids, missing);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    "Proxy {ProxyId} (tenant {TenantId}): could not look up secret ids on save ({ExceptionType}); "
                    + "unbound names will be looked up by name at call time.",
                    proxy.ItemId, tenantId, ex.GetType().Name);
                return (kept, Array.Empty<string>());
            }
        }

        /// <summary>
        /// Import path only (approved PX-9 exception, 2026-10-18): a file from another environment must import even
        /// when a secret is not here yet. Binds only names that match exactly ONE secret; a name with no match is
        /// <c>Missing</c>, one with several is <c>Duplicate</c>, and both stay unbound (the call path then looks the
        /// name up, so adding the secret is the only step left). Nothing is refused. <c>Unavailable</c>: the secret
        /// store could not be read; ids in <paramref name="keep"/> for names still used are kept.
        /// </summary>
        public static async Task<ProxySecretCheck> CheckForImportAsync(
            IProxyVariableResolver? resolver, string tenantId, IReadOnlyList<string> names,
            IReadOnlyDictionary<string, string>? keep, ILogger logger, CancellationToken ct = default)
        {
            var check = new ProxySecretCheck();
            if (names.Count == 0) return check;

            if (resolver is null)
            {
                check.Unavailable = true;
                KeepKnown();
                return check;
            }

            try
            {
                var found = await resolver.LookupAllIdsAsync(names, tenantId, ct)
                    ?? new Dictionary<string, IReadOnlyList<string>>();
                foreach (var name in names)
                {
                    if (!found.TryGetValue(name, out var ids) || ids.Count == 0) check.Missing.Add(name);
                    else if (ids.Count > 1) check.Duplicates.Add(name);
                    else check.Ids[name] = ids[0];
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    "Proxy secret check (tenant {TenantId}): could not look up secret ids ({ExceptionType}); "
                    + "unbound names will be looked up by name at call time.",
                    tenantId, ex.GetType().Name);
                check.Unavailable = true;
                KeepKnown();
            }

            return check;

            void KeepKnown()
            {
                if (keep is null) return;
                foreach (var name in names)
                {
                    if (keep.TryGetValue(name, out var id) && !string.IsNullOrEmpty(id)) check.Ids[name] = id;
                }
            }
        }

        /// <summary>
        /// The names a stored proxy uses that have no saved id (read path, 2026-10-18). Cheap: no secret lookup.
        /// </summary>
        public static List<string> UnboundNames(ProxyDetailEntity proxy) =>
            ProxyVarRef.NamesIn(proxy)
                .Where(n => !proxy.SecretIds.TryGetValue(n, out var id) || string.IsNullOrEmpty(id))
                .ToList();

        public static bool SameIds(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
            a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

        public static Dictionary<string, string> UnknownVariablesError(IReadOnlyList<string> missing) =>
            new(StringComparer.Ordinal)
            {
                ["variables"] = $"No secret named {string.Join(", ", missing)} exists. "
                    + "Create it in Secrets first, or fix the name in {{$VAR.name}}.",
            };
    }

    /// <summary>Result of <see cref="ProxySecretIdBinder.CheckForImportAsync"/>. Names only, never a value.</summary>
    internal sealed class ProxySecretCheck
    {
        public Dictionary<string, string> Ids { get; } = new(StringComparer.Ordinal);

        public List<string> Missing { get; } = new();

        public List<string> Duplicates { get; } = new();

        public bool Unavailable { get; set; }
    }
}
