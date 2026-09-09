using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Resolves the <c>{{$VAR.name}}</c> configuration-variable tokens a proxy carries in its configured
    /// header / query / body-merge values to their Blocks Secrets values, once per forward. This is the only
    /// seam in the proxy module that touches Key Vault; <see cref="Utils.ProxyBodyMerger"/>,
    /// <see cref="ProxyGatewayService"/>'s URL builder, and its header builder all consume the plain
    /// <c>name &rarr; value</c> map it returns, synchronously.
    /// </summary>
    public interface IProxyVariableResolver
    {
        /// <summary>
        /// Resolves every requested variable NAME to its Key Vault value for <paramref name="tenantId"/>.
        /// Throws <see cref="ProxyVariableResolutionException"/> (carrying the offending names) if ANY name is
        /// unknown, locked, access-denied, or the vault is unreachable &mdash; callers must not forward a
        /// partially-resolved request. An empty <paramref name="names"/> set short-circuits with no
        /// <see cref="ISecretService"/> call.
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            IReadOnlyCollection<string> names, string tenantId, CancellationToken ct = default);
    }

    /// <summary>
    /// Thrown when one or more configured <c>{{$VAR.name}}</c> variables could not be resolved. <see cref="Names"/>
    /// carries the offending variable NAMES only &mdash; never a value &mdash; for the failed-forward
    /// execution row and log line.
    /// </summary>
    public sealed class ProxyVariableResolutionException : Exception
    {
        public ProxyVariableResolutionException(IReadOnlyList<string> names)
            : base($"Could not resolve configuration variable(s): {string.Join(", ", names)}")
        {
            Names = names;
        }

        public IReadOnlyList<string> Names { get; }
    }

    /// <summary>Cache TTLs for <see cref="ProxyVariableResolver"/>. Bound from <c>Proxy:VariableResolver:*</c>.</summary>
    public sealed class ProxyVariableResolverOptions
    {
        /// <summary>How long a resolved name &rarr; id mapping is cached per tenant. Ids are stable, so this is long.</summary>
        public TimeSpan IdCacheTtl { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>How long a resolved id &rarr; value is cached per tenant. Short, so a rotation is picked up quickly.</summary>
        public TimeSpan ValueCacheTtl { get; set; } = TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// <see cref="IProxyVariableResolver"/> over the in-process <see cref="ISecretService"/>. Name &rarr; id and
    /// id &rarr; value are both done at forward time behind a per-tenant <see cref="IMemoryCache"/>; the stored
    /// config row only ever holds the verbatim token. See <c>PROXY-PLAN-config-variables.md</c> &sect;3.2.
    /// </summary>
    public sealed class ProxyVariableResolver : IProxyVariableResolver
    {
        private const int FindPageSize = 50;

        private readonly ISecretService _secrets;
        private readonly IMemoryCache _cache;
        private readonly ProxyVariableResolverOptions _options;
        private readonly ILogger<ProxyVariableResolver> _logger;

        public ProxyVariableResolver(
            ISecretService secrets,
            IMemoryCache cache,
            IOptions<ProxyVariableResolverOptions> options,
            ILogger<ProxyVariableResolver> logger)
        {
            _secrets = secrets;
            _cache = cache;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            IReadOnlyCollection<string> names, string tenantId, CancellationToken ct = default)
        {
            var distinct = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
            if (distinct.Count == 0)
            {
                return EmptyMap;
            }

            // The data-plane path (ProxyGatewayController) authenticates with X-Blocks-Key and is
            // [AllowAnonymous], so the ambient BlocksContext is not tenant-scoped. ISecretService reads the
            // tenant (and caller identity for access checks) from that context, so set it for the scope of
            // the resolve and restore afterwards. On the Test path the context is already the caller's.
            var restore = BlocksContext.GetContext();
            var mustSwap = !string.Equals(restore?.TenantId, tenantId, StringComparison.Ordinal);
            if (mustSwap)
            {
                EnterTenantContext(tenantId, restore);
            }

            try
            {
                var missing = new List<string>();

                // --- name -> id (cached; ids are stable) ---
                var nameToId = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var name in distinct)
                {
                    var id = await ResolveIdAsync(name, tenantId, ct);
                    if (id is null)
                    {
                        missing.Add(name);
                    }
                    else
                    {
                        nameToId[name] = id;
                    }
                }

                // --- id -> value (one batched call for every id not already cached) ---
                var idToValue = new Dictionary<string, string>(StringComparer.Ordinal);
                var uncachedIds = new List<string>();
                foreach (var id in nameToId.Values.Distinct(StringComparer.Ordinal))
                {
                    if (_cache.TryGetValue(ValueCacheKey(tenantId, id), out string? cachedValue) && cachedValue is not null)
                    {
                        idToValue[id] = cachedValue;
                    }
                    else
                    {
                        uncachedIds.Add(id);
                    }
                }

                if (uncachedIds.Count > 0)
                {
                    try
                    {
                        var fetched = await _secrets.GetValuesAsync(uncachedIds, ct);
                        foreach (var id in uncachedIds)
                        {
                            if (fetched.TryGetValue(id, out var value) && value is not null)
                            {
                                idToValue[id] = value;
                                _cache.Set(ValueCacheKey(tenantId, id), value, _options.ValueCacheTtl);
                            }
                        }
                    }
                    catch (SecretException ex)
                    {
                        // Unknown / locked / access-denied / vault unreachable: every name backed by an
                        // uncached id in this batch is unresolvable. Names not affected still resolved above.
                        _logger.LogWarning(ex,
                            "Proxy variable resolver: batched value read failed for tenant {TenantId} ({Count} id(s)).",
                            tenantId, uncachedIds.Count);
                        foreach (var (name, id) in nameToId)
                        {
                            if (uncachedIds.Contains(id) && !idToValue.ContainsKey(id))
                            {
                                missing.Add(name);
                            }
                        }
                    }
                }

                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (name, id) in nameToId)
                {
                    if (idToValue.TryGetValue(id, out var value))
                    {
                        result[name] = value;
                    }
                    else if (!missing.Contains(name))
                    {
                        // Id resolved but the value was absent from the batch result with no exception.
                        missing.Add(name);
                    }
                }

                if (missing.Count > 0)
                {
                    var ordered = distinct.Where(missing.Contains).ToList();
                    _logger.LogWarning(
                        "Proxy variable resolver: {Count} variable(s) unresolved for tenant {TenantId}: [{Names}].",
                        ordered.Count, tenantId, string.Join(", ", ordered));
                    throw new ProxyVariableResolutionException(ordered);
                }

                return result;
            }
            finally
            {
                if (mustSwap)
                {
                    BlocksContext.SetContext(restore, false);
                }
            }
        }

        private async Task<string?> ResolveIdAsync(string name, string tenantId, CancellationToken ct)
        {
            var cacheKey = IdCacheKey(tenantId, name);
            if (_cache.TryGetValue(cacheKey, out string? cachedId) && cachedId is not null)
            {
                return cachedId;
            }

            try
            {
                var found = await _secrets.FindAsync(new SecretFilter { Search = name, PageSize = FindPageSize }, ct);
                var match = found.Data?.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
                if (match is null || string.IsNullOrEmpty(match.SecretId))
                {
                    return null;
                }

                _cache.Set(cacheKey, match.SecretId, _options.IdCacheTtl);
                return match.SecretId;
            }
            catch (SecretException ex)
            {
                _logger.LogWarning(ex,
                    "Proxy variable resolver: name lookup failed for a variable of tenant {TenantId}.", tenantId);
                return null;
            }
        }

        private static void EnterTenantContext(string tenantId, BlocksContext? current)
        {
            BlocksContext.SetContext(
                BlocksContext.Create(
                    tenantId: tenantId,
                    roles: [],
                    userId: current?.UserId ?? string.Empty,
                    isAuthenticated: false,
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
                false);
        }

        private static string IdCacheKey(string tenantId, string name) => $"proxyvar:id:{tenantId}:{name}";

        private static string ValueCacheKey(string tenantId, string id) => $"proxyvar:val:{tenantId}:{id}";

        internal static readonly IReadOnlyDictionary<string, string> EmptyMap =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
