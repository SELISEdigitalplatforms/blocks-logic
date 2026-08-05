using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Common.InternalService.Storage
{
    public class FileVersionRepository : IFileVersionRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        public FileVersionRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task CreateFileVersionAsync(FileVersion fileVersion)
        {
            IMongoCollection<FileVersion> entities = _dbContextProvider.GetCollection<FileVersion>(string.Format("{0}s", typeof(FileVersion).Name));
            await entities.InsertOneAsync(fileVersion);
        }

        public async Task<long> GetLatestFileVersionNumberAsync(string fileId)
        {
            var updateFilter = Builders<BsonDocument>.Filter.Eq("_id", fileId);
            var update = Builders<BsonDocument>.Update.Inc<long>("CurrentVersion", 1);
            var nextSequence = await _dbContextProvider.GetCollection<BsonDocument>("Files").FindOneAndUpdateAsync(updateFilter, update, new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = false, ReturnDocument = ReturnDocument.After });

            return nextSequence == null ? 0 : nextSequence["CurrentVersion"].AsInt64;
        }
    }
}
