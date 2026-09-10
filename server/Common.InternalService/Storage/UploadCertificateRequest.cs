using Microsoft.AspNetCore.Http;

namespace Common.InternalService.Storage
{
    public class UploadCertificateRequest
    {
        /// <summary>The certificate file itself. Sent as multipart form data.</summary>
        public IFormFile? Certificate { get; set; }

        /// <summary>
        /// Picks which slot the tenant's certificate is written to: its own, or the external
        /// identity provider's certificate that its tokens are validated against.
        /// </summary>
        public bool IsThirdParty { get; set; }
    }
}
