using Blocks.Genesis;

namespace Common.InternalService.Storage
{
    public class UploadCertificateResponse : BaseResponse
    {
        /// <summary>
        /// Permanent, credential-free URL of the stored certificate. Persisted onto the tenant as
        /// <c>ThirdPartyJwtTokenParameters.PublicCertificatePath</c> and re-read on every external
        /// token validation, so it must never expire.
        /// </summary>
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
