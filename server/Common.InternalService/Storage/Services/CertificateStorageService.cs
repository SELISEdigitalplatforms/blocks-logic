using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
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
    /// </remarks>
    public class CertificateStorageService : ICertificateStorageService
    {
        private readonly IConfigurationRepository _configurationRepository;

        private const string DefaultConfigurationName = "Default";
        private const string CertificateContainerName = "certificates";
        private const string ThirdPartyBlobSuffix = "_3rdparty";
        private const string FallbackContentType = "application/octet-stream";
        private const long MaxCertificateSizeInBytes = 2 * 1024 * 1024;

        // Kept in step with the client-side dropzone. Cryptographic validation is not possible
        // here: a password-protected .pfx can only be parsed with the password, and the client
        // sends that to UpdateTokenValidationParameters rather than to this endpoint.
        private static readonly string[] AllowedExtensions = [".crt", ".der", ".pfx", ".p12"];

        public CertificateStorageService(IConfigurationRepository configurationRepository)
        {
            _configurationRepository = configurationRepository;
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

            var configuration = await _configurationRepository.GetStorageConfigurationByNameAsync(DefaultConfigurationName);

            if (string.IsNullOrWhiteSpace(configuration?.ConnectionString))
            {
                return Error("configuration", "storage_configuration_not_found");
            }

            var certificate = request.Certificate!;

            try
            {
                var containerClient = new BlobContainerClient(configuration.ConnectionString, CertificateContainerName);

                // Genesis reads the certificate back anonymously, so the container has to serve
                // blobs without credentials. An account with public blob access disabled fails
                // here, which is far easier to diagnose than tokens quietly 401ing days later.
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

                var blobClient = containerClient.GetBlobClient(BuildBlobName(tenantId, request.IsThirdParty));

                await using var stream = certificate.OpenReadStream();

                await blobClient.UploadAsync(stream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = string.IsNullOrWhiteSpace(certificate.ContentType)
                            ? FallbackContentType
                            : certificate.ContentType
                    }
                });

                return new UploadCertificateResponse
                {
                    IsSuccess = true,
                    DownloadUrl = blobClient.Uri.ToString()
                };
            }
            catch (RequestFailedException ex)
            {
                return Error("storage", ex.ErrorCode ?? "certificate_upload_failed");
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
