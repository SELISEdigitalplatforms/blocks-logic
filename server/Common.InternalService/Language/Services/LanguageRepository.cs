using Blocks.Genesis;
using MongoDB.Driver;

namespace Common.InternalService.Language
{
    public class LanguageRepository : ILanguageRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "BlocksLanguages";

        public LanguageRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<List<Language>> GetAllLanguagesAsync()
        {
            var collection = _dbContextProvider.GetCollection<Language>(_collectionName);
            return await collection.Find(_ => true).ToListAsync();
        }
    }
}
