namespace Common.InternalService.Storage
{
    public interface ICertificateStorageService
    {
        Task<UploadCertificateResponse> UploadPublicCertificateAsync(UploadCertificateRequest request);
    }
}
