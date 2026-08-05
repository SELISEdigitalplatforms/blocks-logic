using Directory = Common.InternalService.Storage.Directory;

namespace Common.InternalService.Storage
{
    public interface IDirectoryRepository
    {
        Task<Directory> GetDirectoryByItemIDAsync(string itemID);
    }
}
