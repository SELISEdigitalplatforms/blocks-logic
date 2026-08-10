namespace Common.InternalService.Storage
{
    public interface IFileManagementService
    {
        Task<GetPreSignedUrlForUploadResponse> GetPerSignedUrlForUploadAsync(GetPreSignedUrlForUploadRequest request);
        Task<FileResponse?> GetUrlForDownloadFileAsync(GetFileRequest request);
        Task<List<FileResponse>?> GetMultipleUrlsForDownloadFilesAsync(GetFilesRequest request);
    }
}
