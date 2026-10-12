using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using Common.InternalService.Storage;
using Functions.DomainService.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Storage
{
    /// <summary>
    /// Where a function's build artifact lives, and the only way the runner reaches it.
    /// <para>
    /// Every member throws <see cref="FunctionArtifactStoreUnavailableException"/> when the tenant's
    /// storage cannot hold artifacts — none configured, a provider that cannot sign a URL (SFTP), or
    /// storage that failed to open. Callers treat that as "use the image path", never as a failed
    /// build or run: the runner pushes and pulls by registry exactly as it did before artifacts.
    /// </para>
    /// </summary>
    public interface IFunctionArtifactStore
    {
        /// <summary>
        /// A URL the builder may <c>PUT</c> the artifact to, valid for <paramref name="expiry"/>.
        /// Write-only, and for this one object.
        /// </summary>
        Task<string> CreateUploadUrlAsync(
            string tenantId, string artifactId, TimeSpan expiry, CancellationToken cancellationToken = default);

        /// <summary>
        /// A URL the runner may <c>GET</c> the artifact from, valid for <paramref name="expiry"/>.
        /// Read-only, and for this one object. <c>null</c> when the provider can tell the artifact is
        /// not there (Azure checks; S3 does not, and a missing object fails on the runner instead).
        /// </summary>
        Task<string?> CreateDownloadUrlAsync(
            string tenantId, string artifactId, TimeSpan expiry, CancellationToken cancellationToken = default);

        /// <summary>Removes the artifact. Safe to call when it is already gone.</summary>
        Task DeleteAsync(string tenantId, string artifactId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The artifact store, on the tenant's own storage — the same <c>IStorageService</c> every other
    /// Blocks file goes through, chosen by the tenant's <c>Default</c> storage configuration.
    /// <para>
    /// <b>Tenant comes in as an argument</b>, and the store enters that tenant's context itself for
    /// the length of one call. The storage stack reads the tenant from <c>BlocksContext</c> — the
    /// configuration repository for which database, the provider for which container or bucket — and
    /// builds and runs are processed off a Redis stream in the Worker, where there is no caller and
    /// no context, or a context for some other tenant. The previous context is restored afterwards.
    /// </para>
    /// <para>
    /// <b>Nothing happens in the constructor, and nothing is injected but the provider.</b>
    /// <c>ActionFunctionNode</c> depends on this through the invocation service and is one of the
    /// <c>INodeExecutor</c>s the workflow engine takes as a set, so a constructor that throws here —
    /// or a dependency the host forgot to register — fails every workflow, not just Functions. (It
    /// did: it read <c>StorageProvider.ConnectionString</c>, a static that only holds a value while
    /// <c>StorageServiceFactory.GetStorageService</c> is running.) The storage services are resolved
    /// on use instead, where a missing one is "unavailable" like any other storage failure.
    /// </para>
    /// <para>
    /// <b>Known exposure, accepted (user, 2026-10-04):</b> on Azure the tenant container is created
    /// with public blob access by <c>AzureBlobStorageService</c>, so an artifact — the tenant's source
    /// and its dependency tree — is readable by anyone who has its URL without the SAS. The object
    /// name carries the build id (a GUID) so the URL is not guessable, and containers are not
    /// listable anonymously at that access level.
    /// </para>
    /// </summary>
    public sealed class FunctionArtifactStore : IFunctionArtifactStore
    {
        /// <summary>
        /// The folder inside the tenant's container or bucket that holds artifacts, keeping them
        /// apart from the tenant's own files.
        /// </summary>
        public const string KeyPrefix = "blocks-fn-artifacts";

        /// <summary>The tenant storage configuration artifacts use — the same one file uploads default to.</summary>
        public const string StorageConfigurationName = "Default";

        /// <summary>
        /// Providers whose <c>IStorageService</c> can sign a URL the runner can use directly. SFTP's
        /// upload URL is not implemented and its download URL points at a Blocks controller, not the
        /// object, so an SFTP tenant takes the image path.
        /// </summary>
        private static readonly HashSet<string> SigningProviders =
            new(StringComparer.OrdinalIgnoreCase) { "azure", "aws", "s3compatible" };

        private readonly IServiceProvider _services;
        private readonly ILogger<FunctionArtifactStore> _logger;

        public FunctionArtifactStore(IServiceProvider services, ILogger<FunctionArtifactStore> logger)
        {
            _services = services;
            _logger = logger;
        }

        /// <summary>
        /// <c>{tenantId}/{artifactId}.tar</c>.
        /// <para>
        /// The id is the build's own id, which is never reused — so a path is never reused for
        /// different content, and an upload cannot overwrite something another host is reading. The
        /// artifact's <i>content</i> hash is carried separately on the run and verified after
        /// download; the path cannot be keyed by it, because the upload URL has to be signed before
        /// the build knows what it produced.
        /// </para>
        /// </summary>
        internal static string BlobPath(string tenantId, string artifactId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

            // Both halves reach a URL, so neither may contain a separator that could climb out of
            // the tenant's prefix. They are an id and a hex digest in practice; this is the guard
            // that keeps it that way.
            if (tenantId.Contains('/') || tenantId.Contains('\\') || tenantId.Contains(".."))
            {
                throw new ArgumentException("tenantId may not contain a path separator", nameof(tenantId));
            }

            if (artifactId.Contains('/') || artifactId.Contains('\\') || artifactId.Contains(".."))
            {
                throw new ArgumentException("artifactId may not contain a path separator", nameof(artifactId));
            }

            return $"{tenantId.ToLowerInvariant()}/{artifactId}.tar";
        }

        /// <summary>The object's key in the tenant's storage: <c>blocks-fn-artifacts/{tenantId}/{artifactId}.tar</c>.</summary>
        internal static string ObjectKey(string tenantId, string artifactId) =>
            $"{KeyPrefix}/{BlobPath(tenantId, artifactId)}";

        /// <inheritdoc />
        public Task<string> CreateUploadUrlAsync(
            string tenantId, string artifactId, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            var key = ObjectKey(tenantId, artifactId);
            return WithTenantStorageAsync(
                tenantId,
                storage => Task.FromResult(storage.GeneratePreSignedUploadUrlAsync(key, expiry)),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<string?> CreateDownloadUrlAsync(
            string tenantId, string artifactId, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            var key = ObjectKey(tenantId, artifactId);
            return WithTenantStorageAsync(
                tenantId,
                storage => storage.GetDownloadUrlAsync(new DownloadUrlRequest
                {
                    FileName = key,
                    ExpiryDuration = expiry,
                    // Private keeps the SAS on the URL. Public would strip it and hand back the bare
                    // object URL, which only works because the container is public — not something
                    // to lean on.
                    AccessModifier = AccessModifier.Private,
                }),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task DeleteAsync(string tenantId, string artifactId, CancellationToken cancellationToken = default)
        {
            var key = ObjectKey(tenantId, artifactId);
            return WithTenantStorageAsync(tenantId, storage => storage.DeleteFileAsync(key), cancellationToken);
        }

        /// <summary>
        /// Opens the tenant's storage under the tenant's own context, runs <paramref name="use"/>, and
        /// puts the caller's context back. Anything that goes wrong reaching the storage becomes
        /// <see cref="FunctionArtifactStoreUnavailableException"/>, which every caller turns into the
        /// image path; cancellation is left alone.
        /// </summary>
        private async Task<T> WithTenantStorageAsync<T>(
            string tenantId, Func<IStorageService, Task<T>> use, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                var configurations = _services.GetService<IConfigurationRepository>();
                var storageFactory = _services.GetService<IStorageServiceFactory>();
                if (configurations is null || storageFactory is null)
                {
                    throw new FunctionArtifactStoreUnavailableException(
                        "tenant storage services are not registered in this host");
                }

                var configuration = await configurations
                    .GetStorageConfigurationByNameAsync(StorageConfigurationName)
                    .ConfigureAwait(false);

                if (configuration is null)
                {
                    throw new FunctionArtifactStoreUnavailableException(
                        $"tenant '{tenantId}' has no '{StorageConfigurationName}' storage configuration");
                }

                if (string.IsNullOrWhiteSpace(configuration.StorageStrategy)
                    || !SigningProviders.Contains(configuration.StorageStrategy))
                {
                    throw new FunctionArtifactStoreUnavailableException(
                        $"tenant '{tenantId}' storage '{configuration.StorageStrategy}' cannot sign artifact URLs");
                }

                var storage = storageFactory.GetStorageService(configuration);
                return await use(storage).ConfigureAwait(false);
            }
            catch (FunctionArtifactStoreUnavailableException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A provider that fails to open (bad credentials, network, a container it cannot
                // create) or to sign. Logged with the exception for the operator; the message that
                // travels on stays generic, because provider messages can quote account details.
                _logger.LogError(ex, "Tenant {TenantId} storage could not serve a function artifact", tenantId);
                throw new FunctionArtifactStoreUnavailableException(
                    $"tenant '{tenantId}' storage could not serve the artifact ({ex.GetType().Name})");
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
            // Same shape as ProxyVariableResolver's: authenticated, scoped to this tenant, and forced
            // (changeContext: true) so an ambient HttpContext for another identity cannot win.
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
                true);
        }
    }

    /// <summary>
    /// The tenant's storage cannot hold or serve function artifacts right now. Callers fall back to
    /// the image path; it is a <see cref="FunctionUnavailableException"/> only so that, if one ever
    /// escapes to the management API, it answers 503 rather than 500.
    /// </summary>
    public sealed class FunctionArtifactStoreUnavailableException(string message)
        : FunctionUnavailableException(message);
}
