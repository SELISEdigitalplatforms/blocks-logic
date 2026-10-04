using Blocks.Genesis;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Workflow.DomainService.Logging
{
    /// <summary>
    /// Reads stage lines from the <c>Logs</c> Mongo database, one collection per service. Both ways Genesis ships
    /// logs end there: the direct Mongo sink, and the queue path, whose blocks-os LMT worker writes the same base
    /// fields (<c>Timestamp, Level, Message, ServiceName, TenantId, TraceId</c>) into the same database.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public sealed class MongoExecutionLogStore : IExecutionLogStore
    {
        private static readonly BsonRegularExpression StageLinePrefix = new("^\\[wf:");

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;
        private readonly ExecutionLogOptions _options;

        public MongoExecutionLogStore(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret, IOptions<ExecutionLogOptions> options)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _options = options.Value;
        }

        public async Task<IReadOnlyList<RawExecutionLogLine>> QueryAsync(ExecutionLogQuery query, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_blocksSecret.LogConnectionString) || string.IsNullOrWhiteSpace(_blocksSecret.LogDatabaseName))
            {
                throw new ExecutionLogStoreNotConfiguredException();
            }

            var database = _dbContextProvider.GetDatabase(_blocksSecret.LogConnectionString, _blocksSecret.LogDatabaseName);

            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("TraceId", query.TraceId),
                Builders<BsonDocument>.Filter.Eq("TenantId", query.TenantId),
                Builders<BsonDocument>.Filter.Gte("Timestamp", query.FromUtc),
                Builders<BsonDocument>.Filter.Lte("Timestamp", query.ToUtc),
                Builders<BsonDocument>.Filter.Regex("Message", StageLinePrefix));
            var projection = Builders<BsonDocument>.Projection
                .Include("Timestamp").Include("Level").Include("Message").Include("ServiceName");
            var sort = Builders<BsonDocument>.Sort.Ascending("Timestamp");

            // A collection that doesn't exist just returns no rows.
            var perCollection = await Task.WhenAll(_options.Collections
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(async name =>
                {
                    var documents = await database.GetCollection<BsonDocument>(name)
                        .Find(filter)
                        .Project(projection)
                        .Sort(sort)
                        .Limit(query.Limit + 1)
                        .ToListAsync(ct);
                    return documents.Select(d => ToLine(d, name)).ToList();
                }));

            return perCollection.SelectMany(lines => lines).ToList();
        }

        private static RawExecutionLogLine ToLine(BsonDocument document, string collectionName)
        {
            var serviceName = document.TryGetValue("ServiceName", out var service) && service.IsString && service.AsString.Length > 0
                ? service.AsString
                : collectionName;
            return new RawExecutionLogLine(
                ReadTimestamp(document),
                document.TryGetValue("Level", out var level) ? ReadLevel(level) : "Information",
                document.TryGetValue("Message", out var message) && message.IsString ? message.AsString : string.Empty,
                serviceName);
        }

        private static DateTime ReadTimestamp(BsonDocument document)
        {
            if (!document.TryGetValue("Timestamp", out var value)) return DateTime.MinValue;
            if (value.IsValidDateTime) return value.ToUniversalTime();
            if (value.IsString && DateTime.TryParse(value.AsString, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
            return DateTime.MinValue;
        }

        private static string ReadLevel(BsonValue value)
        {
            if (value.IsString) return value.AsString;
            // Serilog's LogEventLevel order, in case a sink stored the numeric value.
            if (value.IsNumeric)
            {
                return value.ToInt32() switch
                {
                    3 => "Warning",
                    4 or 5 => "Error",
                    _ => "Information",
                };
            }
            return "Information";
        }
    }
}
