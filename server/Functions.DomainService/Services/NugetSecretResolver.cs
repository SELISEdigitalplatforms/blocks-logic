using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Resolves secrets through the in-process <c>SeliseBlocks.Secrets.OS</c> domain
    /// (DECISIONS D6). Selected by <c>Functions:SecretResolver = "nuget"</c>.
    /// <para>
    /// <see cref="ISecretService"/> reads the ambient <c>BlocksContext</c> to scope every call
    /// to a tenant, but this resolver runs from the Worker's result consumer — a background
    /// loop with no HTTP request and no ambient context of its own. So each call opens a fresh
    /// DI scope and manufactures a system context for the run's tenant, exactly as
    /// <c>SchedulePublisherService</c> does for its own tenant-scoped background work: no real
    /// end user, an empty token, <c>isAuthenticated: false</c> — this is the platform doing
    /// something on the tenant's behalf, not impersonating anyone.
    /// </para>
    /// <para>
    /// <b>Values live in Azure Key Vault, not Mongo</b> (the SDK ships exactly one
    /// <c>ISecretValueStore</c> implementation, <c>KeyVaultSecretValueStore</c>). Without
    /// <c>KeyVault__*</c> configured, <see cref="ISecretService.GetValuesAsync"/> will fail —
    /// which is exactly DECISIONS.md's open question #3, and why <see cref="BlocksOsHttpSecretResolver"/>
    /// exists as a selectable alternative.
    /// </para>
    /// </summary>
    public class NugetSecretResolver : ISecretResolver
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITenants _tenants;
        private readonly ILogger<NugetSecretResolver> _logger;

        public NugetSecretResolver(
            IServiceScopeFactory scopeFactory, ITenants tenants, ILogger<NugetSecretResolver> logger)
        {
            _scopeFactory = scopeFactory;
            _tenants = tenants;
            _logger = logger;
        }

        public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            IReadOnlyCollection<string> secretIds, string tenantId, CancellationToken cancellationToken = default)
        {
            if (secretIds.Count == 0) return new Dictionary<string, string>();

            try
            {
                var tenant = _tenants.GetTenantByID(tenantId);
                if (tenant is null)
                {
                    _logger.LogWarning("Cannot resolve secrets: unknown tenant {TenantId}", tenantId);
                    return new Dictionary<string, string>();
                }

                var applicationDomain = tenant.Applications?.FirstOrDefault()?.Domain ?? string.Empty;
                var systemContext = BlocksContext.Create(
                    tenantId: tenant.TenantId,
                    roles: [],
                    userId: string.Empty,
                    isAuthenticated: false,
                    requestUri: string.Empty,
                    organizationId: string.Empty,
                    expireOn: DateTime.MinValue,
                    email: string.Empty,
                    permissions: [],
                    userName: string.Empty,
                    phoneNumber: string.Empty,
                    displayName: string.Empty,
                    oauthToken: string.Empty,
                    originalTenantId: tenant.TenantId,
                    applicationDomain: applicationDomain,
                    impersonated: false,
                    impersonationSessionId: string.Empty);

                BlocksContext.SetContext(systemContext, changeContext: false);

                using var scope = _scopeFactory.CreateScope();
                var secretService = scope.ServiceProvider.GetRequiredService<ISecretService>();

                var resolved = await secretService.GetValuesAsync(secretIds, cancellationToken);

                // "Absent, not thrown" is the contract, which means a caller sees the same empty
                // slot whether the secret does not exist, is not readable, or the value store is
                // misconfigured. Say which here — this log is the only place the difference is
                // visible, and without it a run refused for an unresolved reference gives the
                // author nothing to act on.
                var missing = secretIds.Where(id => !resolved.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                {
                    _logger.LogWarning(
                        "Resolved {Resolved}/{Requested} secret(s) for tenant {TenantId}; no value came back for: {MissingIds}. " +
                        "The id is looked up through ISecretService, which derives its own key-vault name — " +
                        "check the secret exists for this tenant and that its value was ever set.",
                        resolved.Count, secretIds.Count, tenantId, string.Join(", ", missing));
                }

                return resolved;
            }
            catch (Exception ex)
            {
                // A stale or inaccessible reference must fail one output action, not the whole
                // run's result processing — the interface contract is "absent, not thrown".
                // The exception type is the useful half: a KeyVault 403 and a missing
                // KeyVault__* configuration are the same empty dictionary to the caller.
                _logger.LogWarning(ex,
                    "Could not resolve {Count} secret(s) for tenant {TenantId} ({ExceptionType}). Ids: {SecretIds}",
                    secretIds.Count, tenantId, ex.GetType().Name, string.Join(", ", secretIds));
                return new Dictionary<string, string>();
            }
        }
    }
}
