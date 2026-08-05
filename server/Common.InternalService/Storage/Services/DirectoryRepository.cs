using Blocks.Genesis;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;
using Directory = Common.InternalService.Storage.Directory;

namespace Common.InternalService.Storage
{
    [ExcludeFromCodeCoverage]
    public class DirectoryRepository : IDirectoryRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        public DirectoryRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<Directory> GetDirectoryByItemIDAsync(string itemID)
        {
            FilterDefinition<Directory> filter = Builders<Directory>.Filter.Eq("_id", itemID);
            var collection = _dbContextProvider.GetCollection<Directory>(string.Format("{0}s", typeof(Directory).Name));
            return await collection.Find(filter).SingleOrDefaultAsync();
        }
    }
}
