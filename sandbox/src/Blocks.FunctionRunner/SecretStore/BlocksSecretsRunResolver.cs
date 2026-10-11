using Blocks.FunctionRunner.Runs;
using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blocks.FunctionRunner.SecretStore
{
    /// <summary>
    /// Resolves a run's secret references through the in-process <c>SeliseBlocks.Secrets.OS</c>
    /// domain — the same store, the same Key Vault and the same tenant registry the control
    /// plane's <c>NugetSecretResolver</c> uses.
    /// <para>
    /// <see cref="ISecretService"/> scopes every call to the ambient <c>BlocksContext</c>, and the
    /// runner has no request and so no ambient context. Each call therefore opens a fresh DI scope
    /// and sets a context for the run's tenant, <b>authenticated</b> — the SDK refuses an
    /// unauthenticated one outright (<c>INVALID_CONTEXT</c>) — and carrying the caller identity the
    /// control plane recorded in the envelope (user, organisation, roles). That keeps the access
    /// rules the secret owner set: a <c>service</c> or <c>both</c> secret is readable by any valid
    /// context in the tenant, and an access-listed <c>api</c> secret only when the run's caller is
    /// on its list (or created it), exactly as when the control plane resolved it on the caller's
    /// own request. The context is set inside an async method, so it cannot leak into the
    /// runner's other work: an AsyncLocal assignment does not flow back out to the caller.
    /// </para>
    /// <para>
    /// The SDK throws on the first unreadable id of a batch rather than leaving it out. So the
    /// batch is tried first — one call is the normal case — and only when it is refused is each
    /// id asked for on its own, so the failure can name every broken variable rather than just
    /// the first.
    /// </para>
    /// </summary>
    public sealed class BlocksSecretsRunResolver : IRunSecretResolver
    {
        /// <summary>The SDK's own batch ceiling.</summary>
        private const int BatchSize = 50;

        /// <summary>
        /// How long a lookup may take before the run is given back as a retryable failure. A
        /// hung Key Vault must not hold a sandbox slot for the rest of the lease.
        /// </summary>
        internal TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITenants _tenants;
        private readonly ILogger<BlocksSecretsRunResolver> _logger;

        public BlocksSecretsRunResolver(
            IServiceScopeFactory scopeFactory, ITenants tenants, ILogger<BlocksSecretsRunResolver> logger)
        {
            _scopeFactory = scopeFactory;
            _tenants = tenants;
            _logger = logger;
        }

        public async Task<SecretLookup> ResolveAsync(
            string tenantId,
            EnvSecretReferences.Caller caller,
            IReadOnlyCollection<string> secretIds,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(secretIds);

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var unresolved = new Dictionary<string, string>(StringComparer.Ordinal);
            if (secretIds.Count == 0) return new SecretLookup(values, unresolved);

            Tenant? tenant;
            try
            {
                tenant = _tenants.GetTenantByID(tenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Could not look up tenant {TenantId} to resolve secrets ({ExceptionType})", tenantId, ex.GetType().Name);
                throw new SecretStoreUnavailableException("the tenant registry could not be read", ex);
            }

            if (tenant is null)
            {
                foreach (var id in secretIds) unresolved[id] = SecretUnresolvedReasons.UnknownTenant;
                return new SecretLookup(values, unresolved);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            try
            {
                await ResolveInContextAsync(tenant, caller, secretIds, values, unresolved, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SecretStoreUnavailableException(
                    $"the secret store did not answer within {Timeout.TotalSeconds:0}s");
            }

            if (unresolved.Count > 0)
            {
                // Ids and reasons only — this is the only place the reason behind a failed run is
                // visible to an operator, and it is written without a single value in it.
                _logger.LogWarning(
                    "Resolved {Resolved}/{Requested} secret(s) for tenant {TenantId}; unresolved: {Unresolved}",
                    values.Count, secretIds.Count, tenantId,
                    string.Join(", ", unresolved.Select(u => $"{u.Key} ({u.Value})")));
            }

            return new SecretLookup(values, unresolved);
        }

        private async Task ResolveInContextAsync(
            Tenant tenant,
            EnvSecretReferences.Caller caller,
            IReadOnlyCollection<string> secretIds,
            Dictionary<string, string> values,
            Dictionary<string, string> unresolved,
            CancellationToken token)
        {
            var applicationDomain = tenant.Applications?.FirstOrDefault()?.Domain ?? string.Empty;
            var context = BlocksContext.Create(
                tenantId: tenant.TenantId,
                roles: caller.Roles,
                userId: caller.UserId ?? string.Empty,
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: caller.OrganizationId ?? string.Empty,
                expireOn: DateTime.UtcNow.AddMinutes(5),
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

            // changeContext: true — with no HttpContext here the async-local is what GetContext
            // reads, and forcing it means nothing else can be mistaken for this run's caller.
            BlocksContext.SetContext(context, changeContext: true);

            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

            foreach (var chunk in secretIds.Distinct(StringComparer.Ordinal).Chunk(BatchSize))
            {
                try
                {
                    var batch = await secrets.GetValuesAsync(chunk, token).ConfigureAwait(false);
                    foreach (var id in chunk)
                    {
                        if (batch.TryGetValue(id, out var value) && value is not null) values[id] = value;
                        else unresolved[id] = SecretUnresolvedReasons.NoValue;
                    }
                }
                catch (SecretException ex) when (ex is not SecretVaultException)
                {
                    // Something in this batch is unreadable. Ask for each id alone to learn which.
                    foreach (var id in chunk)
                    {
                        try
                        {
                            values[id] = await secrets.GetValueAsync(id, token).ConfigureAwait(false);
                        }
                        catch (SecretException one) when (one is not SecretVaultException)
                        {
                            unresolved[id] = Reason(one);
                        }
                        catch (Exception one) when (one is not OperationCanceledException)
                        {
                            throw Unavailable(tenant.TenantId, one);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw Unavailable(tenant.TenantId, ex);
                }
            }
        }

        private SecretStoreUnavailableException Unavailable(string tenantId, Exception ex)
        {
            // The type and the SDK's reason code are enough to tell a Key Vault 403 from a Mongo
            // timeout; the message is left out because this path is reached after other ids in
            // the same run may already have resolved, and nothing here vouches for what an
            // arbitrary exception's text contains.
            _logger.LogWarning(
                "The secret store failed for tenant {TenantId} ({ExceptionType}{Reason})",
                tenantId, ex.GetType().Name, ex is SecretException s && s.ReasonCode is { } r ? $", {r}" : string.Empty);
            return new SecretStoreUnavailableException(
                ex is SecretVaultException
                    ? "the key vault could not be read"
                    : $"the secret store could not be read ({ex.GetType().Name})",
                ex);
        }

        private static string Reason(SecretException ex) => ex switch
        {
            SecretNotFoundException { ReasonCode: "VALUE_MISSING" } => SecretUnresolvedReasons.NoValue,
            SecretNotFoundException => SecretUnresolvedReasons.NotFound,
            SecretStateException { ReasonCode: "STATUS_DELETED" } => SecretUnresolvedReasons.Deleted,
            SecretStateException => SecretUnresolvedReasons.Locked,
            SecretAccessDeniedException { ReasonCode: "INVALID_CONTEXT" } => SecretUnresolvedReasons.UnknownTenant,
            SecretAccessDeniedException => SecretUnresolvedReasons.AccessDenied,
            SecretValidationException => SecretUnresolvedReasons.Invalid,
            _ => SecretUnresolvedReasons.AccessDenied,
        };
    }
}
