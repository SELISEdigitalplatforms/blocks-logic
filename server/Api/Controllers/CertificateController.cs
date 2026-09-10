using Common.InternalService.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class CertificateController : ControllerBase
    {
        private readonly ICertificateStorageService _certificateStorageService;

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
        [Authorize]
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
