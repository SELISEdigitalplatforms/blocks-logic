using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
        /// <param name="knownIds">
        /// The proxy's stored <c>name &rarr; secret id</c> pairs, written when it was saved (PX-9). A name found here
        /// is read by id at once, with no search; only a name missing from it (a draft under Test, or a proxy saved
        /// before 2026-10-07) falls back to a name lookup.
        /// </param>
        Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            IReadOnlyCollection<string> names, string tenantId,
            IReadOnlyDictionary<string, string>? knownIds = null, CancellationToken ct = default);

        /// <summary>
        /// Save-time lookup (PX-9): the exact secret id for each name, reading every page of the search so a
        /// match is never missed behind the first 50 results. Names not found are left out of the result.
        /// Throws only when the secret store itself cannot be read.
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> LookupIdsAsync(
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

    /// <summary>Cache TTL for <see cref="ProxyVariableResolver"/>. Bound from <c>Proxy:VariableResolver:*</c>.</summary>
    public sealed class ProxyVariableResolverOptions
    {
        /// <summary>How long a resolved name &rarr; id mapping is cached per tenant. Ids are stable, so this is long.</summary>
        public TimeSpan IdCacheTtl { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// <see cref="IProxyVariableResolver"/> over the in-process <see cref="ISecretService"/>. Name &rarr; id is cached
    /// per tenant (an id is not a secret). Id &rarr; value is read from the vault on EVERY call and never cached
    /// (user, 2026-10-07: "never ever cache any secret"), so every use runs the caller's access check. The stored
    /// config row only ever holds the verbatim token. See <c>PROXY-PLAN-config-variables.md</c> &sect;3.2.
    /// <para>
    /// This resolver is a singleton (it sits behind the singleton Proxy/Workflow engine — gateway, node
    /// executors, Worker consumers), but <c>AddBlocksSecrets()</c> registers <see cref="ISecretService"/>
    /// (and <c>SecretStoreContext</c>) as scoped, reading the request-scoped <c>BlocksContext</c>. So it
    /// takes <see cref="IServiceScopeFactory"/> instead and opens a fresh DI scope per
    /// <see cref="ResolveAsync"/> call to resolve <see cref="ISecretService"/>, the same pattern
    /// <c>NugetSecretResolver</c> uses for the same singleton/scoped mismatch.
    /// </para>
    /// </summary>
    public sealed class ProxyVariableResolver : IProxyVariableResolver
    {
        private const int FindPageSize = 50;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMemoryCache _cache;
        private readonly ProxyVariableResolverOptions _options;
        private readonly ILogger<ProxyVariableResolver> _logger;

        public ProxyVariableResolver(
            IServiceScopeFactory scopeFactory,
            IMemoryCache cache,
            IOptions<ProxyVariableResolverOptions> options,
            ILogger<ProxyVariableResolver> logger)
        {
            _scopeFactory = scopeFactory;
            _cache = cache;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            IReadOnlyCollection<string> names, string tenantId,
            IReadOnlyDictionary<string, string>? knownIds = null, CancellationToken ct = default)
        {
            var distinct = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
            if (distinct.Count == 0)
            {
                return EmptyMap;
            }

            // The data-plane path (ProxiesController.Gateway) authenticates with X-Blocks-Key and is
            // [AllowAnonymous], so the ambient BlocksContext is not tenant-scoped. ISecretService reads the
            // tenant (and caller identity for access checks) from that context, so set it for the scope of
            // the resolve and restore afterwards. On the Test path the context is already the caller's.
            var restore = BlocksContext.GetContext();
            // Swap in a synthetic tenant-scoped context unless the ambient one is ALREADY usable by
            // ISecretService — i.e. authenticated AND scoped to this tenant. On the data-plane path the Blocks
            // tenant-resolution middleware leaves an ambient context that carries the right TenantId but
            // IsAuthenticated == false, which SecretAuthorizationService.ResolveContext() rejects outright, so a
            // tenant-only check here is not enough.
            var mustSwap = restore is null
                || !restore.IsAuthenticated
                || !string.Equals(restore.TenantId, tenantId, StringComparison.Ordinal);

            if (mustSwap)
            {
                EnterTenantContext(tenantId, restore);
            }

            try
            {
                // ISecretService (and SecretStoreContext) are scoped. Open a request-sized scope for this
                // resolve so the singleton resolver does not capture them — Development ValidateScopes
                // rejects that graph at host build; Production would silently keep one context forever.
                using var scope = _scopeFactory.CreateScope();
                var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

                var missing = new List<string>();

                // --- name -> id (cached; ids are stable) ---
                var nameToId = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var name in distinct)
                {
                    // Stored at save (PX-9): no search on the call path. Only an unknown name looks it up.
                    var id = knownIds is not null && knownIds.TryGetValue(name, out var known) && !string.IsNullOrEmpty(known)
                        ? known
                        : await ResolveIdAsync(secrets, name, tenantId, ct);
                    if (id is null)
                    {
                        missing.Add(name);
                    }
                    else
                    {
                        nameToId[name] = id;
                    }
                }

                // --- id -> value (one batched call; never cached — every use runs the access check) ---
                var idToValue = new Dictionary<string, string>(StringComparer.Ordinal);
                var ids = nameToId.Values.Distinct(StringComparer.Ordinal).ToList();

                if (ids.Count > 0)
                {
                    try
                    {
                        var fetched = await secrets.GetValuesAsync(ids, ct);

                        foreach (var id in ids)
                        {
                            if (fetched.TryGetValue(id, out var value) && value is not null)
                            {
                                idToValue[id] = value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Unknown / locked / access-denied / vault unreachable, including non-SecretException
                        // infra failures (Key Vault auth, network, DI): every name backed by an id in
                        // this batch is unresolvable. Names not affected still resolved above.
                        _logger.LogWarning(ex,
                            "Proxy variable resolver: batched value read failed for tenant {TenantId} ({Count} id(s)).",
                            tenantId, ids.Count);
                        foreach (var (name, id) in nameToId)
                        {
                            if (ids.Contains(id) && !idToValue.ContainsKey(id))
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
                    // Restore the ambient context. When there was none (the data-plane path) this clears it;
                    // changeContext mirrors whether a real context is being put back.
                    BlocksContext.SetContext(restore, restore is not null);
                }
            }
        }

        private async Task<string?> ResolveIdAsync(
            ISecretService secrets, string name, string tenantId, CancellationToken ct)
        {
            var cacheKey = IdCacheKey(tenantId, name);
            if (_cache.TryGetValue(cacheKey, out string? cachedId) && cachedId is not null)
            {
                return cachedId;
            }

            try
            {
                var id = await FindExactIdAsync(secrets, name, ct);
                if (id is null)
                {
                    return null;
                }

                _cache.Set(cacheKey, id, _options.IdCacheTtl);
                return id;
            }
            catch (SecretException ex)
            {
                _logger.LogWarning(ex,
                    "Proxy variable resolver: name lookup failed for a variable of tenant {TenantId}.",
                    tenantId);
                return null;
            }
            catch (Exception ex)
            {
                // Widened from SecretException so a non-SecretException failure (Mongo / context / DI) surfaces
                // in the log instead of bubbling up as an opaque 500 or being silently reported as "unresolved".
                _logger.LogError(ex,
                    "Proxy variable resolver: unexpected error during name lookup for a variable of tenant {TenantId}.",
                    tenantId);
                return null;
            }
        }

        /// <summary>Upper bound on pages read for one name: 100 × 50 = 5,000 secrets whose name or description contains it.</summary>
        private const int MaxFindPages = 100;

        /// <summary>
        /// The Secrets service only searches "name or description contains, any case", newest first, one page at a
        /// time. Reading only the first page missed an exact match on page 2 (PX-9), so every page is read until the
        /// exact name is found or the results run out.
        /// </summary>
        private static async Task<string?> FindExactIdAsync(ISecretService secrets, string name, CancellationToken ct)
        {
            for (var page = 1; page <= MaxFindPages; page++)
            {
                var found = await secrets.FindAsync(
                    new SecretFilter { Search = name, PageSize = FindPageSize, PageNumber = page }, ct);
                var data = found.Data ?? Array.Empty<SecretResult>();

                var match = data.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
                if (match is not null && !string.IsNullOrEmpty(match.SecretId))
                {
                    return match.SecretId;
                }

                if (data.Count < FindPageSize || (long)page * FindPageSize >= found.TotalCount)
                {
                    return null;
                }
            }

            return null;
        }

        public async Task<IReadOnlyDictionary<string, string>> LookupIdsAsync(
            IReadOnlyCollection<string> names, string tenantId, CancellationToken ct = default)
        {
            var distinct = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            if (distinct.Count == 0)
            {
                return ids;
            }

            var restore = BlocksContext.GetContext();
            var mustSwap = restore is null
                || !restore.IsAuthenticated
                || !string.Equals(restore.TenantId, tenantId, StringComparison.Ordinal);
            if (mustSwap)
            {
                EnterTenantContext(tenantId, restore);
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
                foreach (var name in distinct)
                {
                    var id = await FindExactIdAsync(secrets, name, ct);
                    if (id is not null)
                    {
                        ids[name] = id;
                        _cache.Set(IdCacheKey(tenantId, name), id, _options.IdCacheTtl);
                    }
                }

                return ids;
            }
            finally
            {
                if (mustSwap)
                {
                    BlocksContext.SetContext(restore, restore is not null);
                }
            }
        }

        private static void EnterTenantContext(string tenantId, BlocksContext? current)
        {
            // isAuthenticated MUST be true: Blocks.Secrets' SecretAuthorizationService.ResolveContext() rejects
            // any context with !IsAuthenticated || blank TenantId ("INVALID_CONTEXT"), and every ISecretService
            // read (FindAsync / GetValuesAsync) calls it first. This synthetic context lives only for the scope
            // of one resolve on the [AllowAnonymous] data-plane path and is restored in the finally below.
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
                // changeContext: true — force BlocksContext.GetContext() to return this async-local context
                // even if the ambient HttpContext still carries an authenticated ClaimsIdentity, so the resolve
                // is always tenant-scoped to the proxy's tenant and never the caller's.
                true);
        }

        private static string IdCacheKey(string tenantId, string name) => $"proxyvar:id:{tenantId}:{name}";

        internal static readonly IReadOnlyDictionary<string, string> EmptyMap =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
