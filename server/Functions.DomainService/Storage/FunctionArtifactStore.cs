using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Common.InternalService.Storage;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Storage
{
    /// <summary>
    /// Where a function's build artifact lives, and the only way the runner reaches it.
    /// </summary>
    public interface IFunctionArtifactStore
    {
        /// <summary>
        /// A URL the builder may <c>PUT</c> the artifact to, valid for <paramref name="expiry"/>.
        /// Write-only, and for this one blob.
        /// </summary>
        string CreateUploadUrl(string tenantId, string artifactId, TimeSpan expiry);

        /// <summary>
        /// A URL the runner may <c>GET</c> the artifact from, valid for <paramref name="expiry"/>.
        /// Read-only, and for this one blob. <c>null</c> when the artifact is not there — which the
        /// caller must treat as "this function cannot run", not as an empty download.
        /// </summary>
        Task<string?> CreateDownloadUrlAsync(
            string tenantId, string artifactId, TimeSpan expiry, CancellationToken cancellationToken = default);

        Task<bool> ExistsAsync(string tenantId, string artifactId, CancellationToken cancellationToken = default);

        /// <summary>Removes the artifact. Safe to call when it is already gone.</summary>
        Task DeleteAsync(string tenantId, string artifactId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The artifact store, on Azure Blob.
    /// <para>
    /// Deliberately <b>not</b> <c>IStorageService</c> / <c>FileManagementService</c>, for two reasons
    /// that are both load-bearing:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>Tenant comes in as an argument, never from ambient context.</b>
    /// <c>AzureBlobStorageService</c> names its container from <c>BlocksContext.GetContext()</c>.
    /// Builds and runs are processed off a Redis stream in the Worker, where there is no caller and
    /// no ambient context — so that would resolve to nothing, or, worse, to whichever tenant happened
    /// to be on the thread. Every other method in this domain already passes <c>tenantId</c>
    /// explicitly; so does this one.
    /// </item>
    /// <item>
    /// <b>Its own private container.</b> The tenant content container is created with
    /// <see cref="PublicAccessType.Blob"/>, which makes every blob in it anonymously readable — the
    /// <c>Private/</c> path prefix there is a naming convention, not a boundary. An artifact holds
    /// the tenant's source and its whole dependency tree, so it goes in a container created with
    /// <see cref="PublicAccessType.None"/> and is reachable only through a short-lived SAS scoped to
    /// the single blob.
    /// </item>
    /// </list>
    /// <para>
    /// The runner therefore holds no storage credential of any kind — the same posture that keeps it
    /// from holding a database credential. It is handed a URL that can do one thing, to one blob, for
    /// a few minutes.
    /// </para>
    /// </summary>
    public sealed class FunctionArtifactStore : IFunctionArtifactStore
    {
        /// <summary>
        /// One container for every tenant's artifacts. Isolation is the <c>tenantId</c> path segment
        /// plus a SAS scoped to one blob, not a container boundary — a container per tenant would add
        /// a provisioning step to every signup and buy nothing a per-blob SAS does not already give.
        /// </summary>
        public const string ContainerName = "blocks-fn-artifacts";

        private readonly BlobContainerClient _container;
        private readonly ILogger<FunctionArtifactStore> _logger;

        public FunctionArtifactStore(ILogger<FunctionArtifactStore> logger)
        {
            _logger = logger;
            _container = new BlobContainerClient(StorageProvider.ConnectionString, ContainerName);
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

        /// <summary>
        /// Created on first use with public access off.
        /// <para>
        /// <see cref="PublicAccessType.None"/> is passed explicitly rather than left to the account
        /// default: an account that permits public containers would otherwise decide this, and the
        /// one thing this container must never be is readable without a SAS.
        /// </para>
        /// </summary>
        private async Task EnsureContainerAsync(CancellationToken cancellationToken)
        {
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public string CreateUploadUrl(string tenantId, string artifactId, TimeSpan expiry)
        {
            // Not awaited on the upload path: the builder is about to write, and a container that
            // does not exist yet must exist before the SAS is used. Creating it here keeps the first
            // ever build on a fresh environment from failing on a missing container.
            EnsureContainerAsync(CancellationToken.None).GetAwaiter().GetResult();

            var blob = _container.GetBlobClient(BlobPath(tenantId, artifactId));
            return Sign(blob, BlobSasPermissions.Write | BlobSasPermissions.Create, expiry);
        }

        /// <inheritdoc />
        public async Task<string?> CreateDownloadUrlAsync(
            string tenantId, string artifactId, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            var blob = _container.GetBlobClient(BlobPath(tenantId, artifactId));

            if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                // A signed URL to a blob that is not there would be handed to a runner that then
                // fails on a 404 it cannot explain. Saying so here keeps the reason where the
                // control plane can report it.
                _logger.LogWarning(
                    "Artifact {Hash} for tenant {TenantId} is not in the store; no download URL issued",
                    artifactId, tenantId);
                return null;
            }

            return Sign(blob, BlobSasPermissions.Read, expiry);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(
            string tenantId, string artifactId, CancellationToken cancellationToken = default)
        {
            var blob = _container.GetBlobClient(BlobPath(tenantId, artifactId));
            return await blob.ExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(
            string tenantId, string artifactId, CancellationToken cancellationToken = default)
        {
            var blob = _container.GetBlobClient(BlobPath(tenantId, artifactId));
            await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Signs one blob, for one set of permissions, for a short window.
        /// <para>
        /// <c>Resource = "b"</c> is what scopes it to the single blob rather than the container: a
        /// container-scoped SAS handed to a runner would let it read every tenant's artifact.
        /// </para>
        /// </summary>
        private string Sign(BlobClient blob, BlobSasPermissions permissions, TimeSpan expiry)
        {
            if (!blob.CanGenerateSasUri)
            {
                // Happens when the connection string carries no account key — a managed-identity
                // style connection needs a user-delegation key instead, which is a different call.
                // Failing loudly beats handing back a URL that is not actually signed.
                throw new InvalidOperationException(
                    "the storage connection string cannot sign a SAS; function artifacts need an account key");
            }

            var builder = new BlobSasBuilder
            {
                BlobContainerName = blob.BlobContainerName,
                BlobName = blob.Name,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(expiry),
            };

            builder.SetPermissions(permissions);
            return blob.GenerateSasUri(builder).ToString();
        }
    }
}
