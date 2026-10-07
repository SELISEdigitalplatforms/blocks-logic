using Microsoft.Extensions.Logging;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

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

        public static bool SameIds(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
            a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

        public static Dictionary<string, string> UnknownVariablesError(IReadOnlyList<string> missing) =>
            new(StringComparer.Ordinal)
            {
                ["variables"] = $"No secret named {string.Join(", ", missing)} exists. "
                    + "Create it in Secrets first, or fix the name in {{$VAR.name}}.",
            };
    }
}
