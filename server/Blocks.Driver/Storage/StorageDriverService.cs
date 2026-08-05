using Common.InternalService.Storage;

namespace Blocks.Driver.Storage;

public class StorageDriverService : IStorageDriverService
{
    private readonly IFileManagementService _fileManagementService;

    public StorageDriverService(IFileManagementService fileManagementService)
    {
        _fileManagementService = fileManagementService;
    }

    public Task<GetPreSignedUrlForUploadResponse> GetPreSignedUrlForUploadAsync(GetPreSignedUrlForUploadRequest request)
        => _fileManagementService.GetPerSignedUrlForUploadAsync(request);

    public Task<FileResponse?> GetUrlForDownloadFileAsync(GetFileRequest request)
        => _fileManagementService.GetUrlForDownloadFileAsync(request);

    public Task<List<FileResponse>?> GetMultipleUrlsForDownloadFilesAsync(GetFilesRequest request)
        => _fileManagementService.GetMultipleUrlsForDownloadFilesAsync(request);
}
