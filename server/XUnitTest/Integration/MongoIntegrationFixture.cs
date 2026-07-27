using MongoDB.Bson;
using MongoDB.Driver;

namespace XUnitTest.Integration
{
    // Connects to a local MongoDB (mongodb://localhost:27017), creates a unique
    // throwaway database per test run, pings on construction so failures surface
    // loudly, and drops only its own database on disposal. No secrets, hardcoded
    // localhost only. Intended for repository integration tests.
    public sealed class MongoIntegrationFixture : IDisposable
    {
        public const string ConnectionString = "mongodb://localhost:27017";

        public IMongoClient Client { get; }
        public IMongoDatabase Database { get; }
        public string DatabaseName { get; }

        public MongoIntegrationFixture()
        {
            Client = new MongoClient(ConnectionString);

            // Fail loudly if the local server is not reachable.
            Client.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("ping", 1));

            DatabaseName = "blocks_logic_it_" + Guid.NewGuid().ToString("N");
            Database = Client.GetDatabase(DatabaseName);
        }

        public IMongoCollection<T> GetCollection<T>(string name) => Database.GetCollection<T>(name);

        public void Dispose()
        {
            try
            {
                Client.DropDatabase(DatabaseName);
            }
            catch
            {
                // best effort cleanup; never mask the real test outcome
            }
        }
    }

    [CollectionDefinition("Mongo integration")]
    public sealed class MongoIntegrationCollection : ICollectionFixture<MongoIntegrationFixture>
    {
    }
}
