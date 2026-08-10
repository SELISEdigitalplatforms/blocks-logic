using Common.InternalService.Storage;

namespace Blocks.Driver.Storage;

/// <summary>
/// Service for uploading and downloading files through the internal storage service.
/// </summary>
public interface IStorageDriverService
{
    Task<GetPreSignedUrlForUploadResponse> GetPreSignedUrlForUploadAsync(GetPreSignedUrlForUploadRequest request);
    Task<FileResponse?> GetUrlForDownloadFileAsync(GetFileRequest request);
    Task<List<FileResponse>?> GetMultipleUrlsForDownloadFilesAsync(GetFilesRequest request);
}
