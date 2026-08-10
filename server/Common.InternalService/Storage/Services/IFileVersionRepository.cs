namespace Common.InternalService.Storage
{
    public interface IFileVersionRepository
    {
        Task CreateFileVersionAsync(FileVersion fileVersion);
        Task<long> GetLatestFileVersionNumberAsync(string fileId);
    }
}
