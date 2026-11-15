using Blocks.Genesis;
using Common.InternalService.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>Certificate storage for the tenant, routed as <c>/api/Certificate/{action}</c>.</summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class CertificateController : ControllerBase
    {
        private readonly ICertificateStorageService _certificateStorageService;

        /// <summary>Takes the certificate storage service.</summary>
        public CertificateController(ICertificateStorageService certificateStorageService)
        {
            _certificateStorageService = certificateStorageService;
        }

        /// <summary>
        /// Stores a public certificate for the calling tenant and returns the URL to read it back.
        /// </summary>
        /// <remarks>
        /// The file arrives as multipart form data while the flag arrives on the query string, so
        /// both are bound explicitly - a single complex parameter would be inferred as
        /// <c>[FromBody]</c> and reject the upload. Callers also send a <c>TenantId</c> query
        /// parameter; it is ignored, because the tenant comes from the request context.
        /// </remarks>
        [HttpPost]
        [ProtectedEndPoint("blocks-os::project::mutate-token-validation-params")]
        // The global multipart limit was raised to 50 MiB for webhooks; certificates keep their former 15 MB.
        [RequestFormLimits(MultipartBodyLengthLimit = 15 * 1024 * 1024)]
        public Task<UploadCertificateResponse> UploadCertificate(
            IFormFile? certificate,
            [FromQuery] bool isThirdParty)
        {
            return _certificateStorageService.UploadPublicCertificateAsync(new UploadCertificateRequest
            {
                Certificate = certificate,
                IsThirdParty = isThirdParty
            });
        }
    }
}
