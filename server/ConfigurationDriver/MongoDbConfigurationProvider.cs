using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SeliseBlocks.ConfigurationDriver;

public class MongoDbConfigurationSource : IConfigurationSource
{
    private readonly MongoDbConfigurationOptions _options;
    public MongoDbConfigurationSource(MongoDbConfigurationOptions options)
    {
        _options = options;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new MongoDbConfigurationProvider(_options);
}

public class MongoDbConfigurationProvider : ConfigurationProvider
{
    private readonly MongoDbConfigurationOptions _options;

    public MongoDbConfigurationProvider(MongoDbConfigurationOptions options)
    {
        _options = options;
    }

    public override void Load()
    {
        try
        {
            var client = new MongoClient(_options.ConnectionString);
            var database = client.GetDatabase(_options.DatabaseName);
            var collection = database.GetCollection<BsonDocument>(_options.CollectionName);

            var filter = Builders<BsonDocument>.Filter.Eq("SecretKey", _options.SecretKey);
            var document = collection.Find(filter).FirstOrDefault();

            if (document is null)
            {
                if (!_options.Optional)
                    throw new InvalidOperationException(
                        $"[SeliseBlocks.ConfigurationDriver] No document found in '{_options.CollectionName}' with SecretKey='{_options.SecretKey}'.");
                return;
            }

            if (!document.Contains("KeyPairs") || document["KeyPairs"].BsonType != BsonType.Document)
                return;

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in document["KeyPairs"].AsBsonDocument)
            {
                data[element.Name] = element.Value.BsonType == BsonType.Null
                    ? null
                    : element.Value.ToString();
            }

            Data = data;
        }
        catch (Exception ex) when (_options.Optional)
        {
            Console.Error.WriteLine($"[SeliseBlocks.ConfigurationDriver] Failed to load configuration: {ex.Message}");
        }
    }
}
