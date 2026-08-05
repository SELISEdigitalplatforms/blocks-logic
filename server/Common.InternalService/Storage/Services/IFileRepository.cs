using MongoDB.Bson;
using File = Common.InternalService.Storage.File;

namespace Common.InternalService.Storage
{
    public interface IFileRepository
    {
        Task CreateFileAsync(File file);
        Task<File> GetFileByItemIdAsync(string itemId);
        (IEnumerable<BsonDocument>, FileResponse[]) GetRequiredFiles(IEnumerable<string> fileIds, long? version);
    }
}
