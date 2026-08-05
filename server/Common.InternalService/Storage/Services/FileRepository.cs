using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;
using File = Common.InternalService.Storage.File;

namespace Common.InternalService.Storage
{
    [ExcludeFromCodeCoverage]
    public class FileRepository : IFileRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        public FileRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public (IEnumerable<BsonDocument>, FileResponse[]) GetRequiredFiles(IEnumerable<string> fileIds, long? version)
        {
            var filesCollection = _dbContextProvider.GetCollection<FileResponse>(string.Format("{0}s", typeof(File).Name));

            var filesFilter = Builders<FileResponse>.Filter.In("_id", fileIds);

            var files = filesCollection.Find(filesFilter)
                .Project<FileResponse>(filesProjection)
                .Sort(fileSort)
                .ToEnumerable().ToArray();
            var dataBase = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? string.Empty);

            var fileVersionCollection = dataBase.GetCollection<BsonDocument>("FileVersions");

            var match = Builders<BsonDocument>.Filter.In("FileId", files.Select(f => f.ItemId));

            if (version.HasValue)
            {
                match &= Builders<BsonDocument>.Filter.Eq("No", version);
            }

            var fileVersionAggregates = fileVersionCollection
            .Aggregate()
            .Match(match)
            .Sort(fileVersionAggregateGroupSort)
            .Group(fileVersionAggregateGroup)
            .ToEnumerable();

            return (fileVersionAggregates, files);
        }

        public async Task CreateFileAsync(File file)
        {
            var collection = _dbContextProvider.GetCollection<File>(string.Format("{0}s", typeof(File).Name));
            await collection.InsertOneAsync(file);
        }

        public Task<File> GetFileByItemIdAsync(string itemId)
        {
            var filter = Builders<File>.Filter.Eq(e => e.ItemId, itemId);
            var collection = _dbContextProvider.GetCollection<File>(string.Format("{0}s", typeof(File).Name));
            return collection.Find(filter).SingleOrDefaultAsync();
        }

        private static readonly ProjectionDefinition<FileResponse> filesProjection = Builders<FileResponse>.Projection
               .Include(file => file.AccessModifier)
               .Include(file => file.CreateDate)
               .Include(file => file.CreatedBy)
               .Include(file => file.ItemId)
               .Include(file => file.Language)
               .Include(file => file.MetaData)
               .Include(file => file.Name)
               .Include(file => file.ParentDirectoryID)
               .Include(file => file.SystemName)
               .Include(file => file.Tags)
               .Include(file => file.TenantId)
               .Include(file => file.Type)
               .Include(file => file.TypeString);

        private static readonly SortDefinition<FileResponse> fileSort = Builders<FileResponse>.Sort.Ascending(file => file.ItemId);
        private static readonly SortDefinition<BsonDocument> fileVersionAggregateGroupSort = Builders<BsonDocument>.Sort.Descending("No");

        private static readonly BsonDocument fileVersionAggregateGroup =
                       new BsonDocument
                           {
                                { "_id","$FileId" },
                                { "SizeInBytes", new BsonDocument
                                                 {
                                                     { "$first", "$SizeInBytes" }
                                                 } },
                                { "VersionId", new BsonDocument
                                                 {
                                                     { "$first", "$_id" }
                                                 } },
                                {
                                    "MaxVersion", new BsonDocument
                                                 {
                                                     { "$max", "$No" }
                                                 }
                                }
                           };
    }
}
