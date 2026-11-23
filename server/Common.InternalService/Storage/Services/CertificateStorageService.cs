using Amazon.S3;
using Azure;
using Blocks.Genesis;
using DomainService.Configuration;
using Microsoft.AspNetCore.Http;

namespace Common.InternalService.Storage
{
    /// <summary>
    /// Stores the public certificates that incoming JWTs are validated against.
    /// </summary>
    /// <remarks>
    /// Deliberately bypasses the DMS pipeline behind <see cref="IFileManagementService"/>: there is
    /// no file record, no version and no file id. Two properties drive that choice. The blob name is
    /// derived from the tenant, so the slot is addressable without a lookup and a re-upload replaces
    /// the certificate in place; and the URL handed back has to stay valid indefinitely and be
    /// readable without credentials, because Genesis re-fetches it with a bare
    /// <see cref="System.Net.Http.HttpClient"/> every time it validates an external token. A DMS URL
    /// is a SAS that would expire underneath it.
    /// <para>
    /// The write goes through <see cref="IStorageService.UploadPublicObjectAsync"/> of the tenant's Default storage
    /// configuration, so it works on every provider that has public read (Azure, AWS, S3-compatible); an SFTP
    /// configuration is answered with a clear error. The shared <c>certificates</c> container is kept.
    /// </para>
    /// </remarks>
    public class CertificateStorageService : ICertificateStorageService
    {
        private readonly IStorageConfigurationProvider _storageConfigurations;
        private readonly IStorageServiceFactory _storageServiceFactory;

        private const string DefaultConfigurationName = "Default";
        private const string CertificateContainerName = "certificates";
        private const string ThirdPartyBlobSuffix = "_3rdparty";
        private const string FallbackContentType = "application/octet-stream";
        private const long MaxCertificateSizeInBytes = 2 * 1024 * 1024;

        // Kept in step with the client-side dropzone. Cryptographic validation is not possible
        // here: a password-protected .pfx can only be parsed with the password, and the client
        // sends that to UpdateTokenValidationParameters rather than to this endpoint.
        private static readonly string[] AllowedExtensions = [".crt", ".der", ".pfx", ".p12"];

        public CertificateStorageService(IStorageConfigurationProvider storageConfigurations, IStorageServiceFactory storageServiceFactory)
        {
            _storageConfigurations = storageConfigurations;
            _storageServiceFactory = storageServiceFactory;
        }

        public async Task<UploadCertificateResponse> UploadPublicCertificateAsync(UploadCertificateRequest request)
        {
            // Taken from the request context, never from the caller. The blob name is derived from
            // it, so a caller-supplied tenant would let anyone overwrite another tenant's
            // certificate and mint tokens that tenant's services accept.
            var tenantId = BlocksContext.GetContext()?.TenantId;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Error("tenant", "tenant_context_missing");
            }

            var rejection = ValidateCertificate(request.Certificate);

            if (rejection is not null)
            {
                return rejection;
            }

            var configuration = await _storageConfigurations.GetByNameAsync(DefaultConfigurationName);

            if (configuration is null)
            {
                return Error("configuration", "storage_configuration_not_found");
            }

            var certificate = request.Certificate!;

            try
            {
                IStorageService storage;

                try
                {
                    storage = _storageServiceFactory.GetStorageService(configuration);
                }
                catch (ArgumentException)
                {
                    // The factory does not know the configuration's storage strategy.
                    return Error("configuration", "storage_configuration_not_found");
                }

                // Genesis reads the certificate back anonymously, so the container has to serve
                // objects without credentials. The provider sets that up (container access level,
                // or a bucket policy on the one bucket); a store that refuses fails here, which is
                // far easier to diagnose than tokens quietly 401ing days later.
                var blobName = BuildBlobName(tenantId, request.IsThirdParty);

                PublicObjectRules.Validate(CertificateContainerName, blobName);

                await using var stream = certificate.OpenReadStream();

                var url = await storage.UploadPublicObjectAsync(
                    CertificateContainerName,
                    blobName,
                    stream,
                    string.IsNullOrWhiteSpace(certificate.ContentType)
                        ? FallbackContentType
                        : certificate.ContentType);

                return new UploadCertificateResponse
                {
                    IsSuccess = true,
                    DownloadUrl = url
                };
            }
            catch (PublicObjectStorageException ex)
            {
                return Error("storage", ex.ErrorCode);
            }
            catch (NotSupportedException)
            {
                return Error("storage", PublicObjectStorageException.NotSupportedCode);
            }
            catch (RequestFailedException ex)
            {
                return Error("storage", ex.ErrorCode ?? "certificate_upload_failed");
            }
            catch (AmazonS3Exception ex)
            {
                return Error("storage", string.IsNullOrEmpty(ex.ErrorCode) ? "certificate_upload_failed" : ex.ErrorCode);
            }
        }

        /// <summary>
        /// Derives the blob a tenant's certificate lives in. Deterministic on purpose: the slot can
        /// be addressed from the tenant id alone, and re-uploading replaces the certificate rather
        /// than accumulating copies. The suffix keeps the external provider's certificate from
        /// overwriting the tenant's own.
        /// </summary>
        public static string BuildBlobName(string tenantId, bool isThirdParty) =>
            isThirdParty ? $"{tenantId}{ThirdPartyBlobSuffix}" : tenantId;

        private static UploadCertificateResponse? ValidateCertificate(IFormFile? certificate)
        {
            if (certificate is null || certificate.Length == 0)
            {
                return Error("certificate", "certificate_file_is_required");
            }

            if (certificate.Length > MaxCertificateSizeInBytes)
            {
                return Error("certificate", "certificate_exceeds_maximum_size");
            }

            var extension = Path.GetExtension(certificate.FileName);

            return AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                ? null
                : Error("certificate", "unsupported_certificate_extension");
        }

        private static UploadCertificateResponse Error(string field, string message) =>
            new()
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { field, message } }
            };
    }
}
