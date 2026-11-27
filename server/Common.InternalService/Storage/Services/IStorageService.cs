using Microsoft.AspNetCore.Http;

namespace Common.InternalService.Storage
{
    public interface IStorageService
    {
        Task<IEnumerable<string>> ListFilesAsync();
        Task<bool> DeleteFileAsync(string fileInfo);
        string GeneratePreSignedUploadUrlAsync(string fileName, TimeSpan expiry);
        Task<string?> GetDownloadUrlAsync(DownloadUrlRequest request);
        Task<Stream?> DownloadFileAsync(string fileName, string? tenantId = null, string? itemId = null, string? versionId = null);
        Task<bool> UploadFileToSftpAsync(string fileName, string tenantId, string itemId, string versionId, IFormFile file);

        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="key"/> in <paramref name="container"/> and returns the
        /// object's permanent public URL: readable without credentials and without an expiry. The container is named by
        /// the caller, not derived from the tenant, so several tenants can share one (the <c>certificates</c> container).
        /// Public read is set per provider: the container's access level on Azure, a bucket policy on that one bucket on
        /// S3. An existing object at the key is replaced. Providers without public read (SFTP) throw
        /// <see cref="NotSupportedException"/>.
        /// </summary>
        Task<string> UploadPublicObjectAsync(string container, string key, Stream content, string contentType);

        /// <summary>
        /// Opens a stream over the object at <paramref name="key"/> in <paramref name="container"/> with the service's own
        /// credentials, or returns null when there is no such object. Providers without named containers (SFTP) throw
        /// <see cref="NotSupportedException"/>.
        /// </summary>
        Task<Stream?> OpenReadAsync(string container, string key);
    }
}
