using System.Globalization;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Common.InternalService.Monitor
{
    public class MonitorIncidentRepoService : IMonitorIncidentRepoService
    {
        private readonly ILogger<MonitorIncidentRepoService> _logger;
        private readonly IMongoCollection<MonitorIncident> _monitorIncidentsCollection;

        public MonitorIncidentRepoService(
            ILogger<MonitorIncidentRepoService> logger,
            IDbContextProvider dbContextProvider,
            IBlocksSecret blocksSecret)
        {
            var db = dbContextProvider.GetDatabase(blocksSecret.DatabaseConnectionString, blocksSecret.RootDatabaseName);

            _logger = logger;
            _monitorIncidentsCollection = db.GetCollection<MonitorIncident>("MonitorIncidents");
        }

        private static SortDefinition<MonitorIncident> BuildSortDefinition(
            string? sortProperty,
            bool sortIsDescending)
        {
            var sortBuilder = Builders<MonitorIncident>.Sort;
            return (sortProperty ?? string.Empty).ToLowerInvariant() switch
            {
                "status" => sortIsDescending
                    ? sortBuilder.Descending(x => x.IsResolved)
                    : sortBuilder.Ascending(x => x.IsResolved),
                "laststatuscode" => sortIsDescending
                    ? sortBuilder.Descending(x => x.LastStatusCode)
                    : sortBuilder.Ascending(x => x.LastStatusCode),
                "rootcause" => sortIsDescending
                    ? sortBuilder.Descending(x => x.FailureReason)
                    : sortBuilder.Ascending(x => x.FailureReason),
                "started_time" => sortIsDescending
                    ? sortBuilder.Descending(x => x.StartTime)
                    : sortBuilder.Ascending(x => x.StartTime),
                "end_time" => sortIsDescending
                    ? sortBuilder.Descending(x => x.EndTime)
                    : sortBuilder.Ascending(x => x.EndTime),
                "duration" => sortIsDescending
                    ? sortBuilder.Descending(x => x.StartTime)
                    : sortBuilder.Ascending(x => x.StartTime),
                _ => sortBuilder.Descending(x => x.StartTime),
            };
        }

        public async Task<List<MonitorIncident>> GetIncidentsByMonitorIdAsync(MonitorConfiguration monitor, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = true)
        {
            try
            {
                var filter = Builders<MonitorIncident>.Filter.Eq(i => i.MonitorId, monitor.ItemId);

                var incidents = await _monitorIncidentsCollection
                    .Find(filter)
                    .Sort(BuildSortDefinition(sortProperty, sortIsDescending))
                    .Skip((pageNumber - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync();

                _logger.LogInformation("Fetched {Count} incidents for MonitorId {MonitorId}", incidents.Count, monitor.ItemId);

                return incidents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching incidents for MonitorId {MonitorId}", monitor.ItemId);
                return new List<MonitorIncident>();
            }
        }

        private static BsonArray BuildDowntimeFacetPipeline(DateTime rangeStart) => new BsonArray
        {
            new BsonDocument("$match", new BsonDocument("StartTime", new BsonDocument("$gte", rangeStart))),
            new BsonDocument("$project", new BsonDocument("durationMs", new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$ne", new BsonArray { "$EndTime", BsonNull.Value }),
                new BsonDocument("$subtract", new BsonArray { "$EndTime", "$StartTime" }),
                new BsonDocument("$subtract", new BsonArray { "$$NOW", "$StartTime" })
            })))
            ,
            new BsonDocument("$group", new BsonDocument { { "_id", BsonNull.Value }, { "totalDurationMs", new BsonDocument("$sum", "$durationMs") } })
        };

        public async Task<Dictionary<string, (long TotalDurationMs, long IncidentCount)>> GetDowntimeAndCountByDateRangesAsync(string monitorId, Dictionary<string, int> rangesInDays)
        {
            try
            {
                var now = DateTime.UtcNow;

                var facetDoc = new BsonDocument();
                foreach (var kvp in rangesInDays)
                {
                    string rangeName = kvp.Key;
                    DateTime rangeStart = now.AddDays(-kvp.Value);

                    facetDoc[rangeName] = new BsonArray
                    {
                        new BsonDocument("$match", new BsonDocument
                        {
                            { "MonitorId", monitorId },
                            { "StartTime", new BsonDocument("$gte", rangeStart) }
                        }),
                        new BsonDocument("$project", new BsonDocument
                        {
                            { "durationMs", new BsonDocument("$cond", new BsonArray
                                {
                                    new BsonDocument("$ne", new BsonArray { "$EndTime", BsonNull.Value }),
                                    new BsonDocument("$subtract", new BsonArray { "$EndTime", "$StartTime" }),
                                    new BsonDocument("$subtract", new BsonArray { "$$NOW", "$StartTime" })
                                })
                            }
                        }),
                        new BsonDocument("$group", new BsonDocument
                        {
                            { "_id", BsonNull.Value },
                            { "totalDurationMs", new BsonDocument("$sum", "$durationMs") },
                            { "incidentCount", new BsonDocument("$sum", 1) }
                        })
                    };
                }

                var pipeline = new[] { new BsonDocument("$facet", facetDoc) };
                var result = await _monitorIncidentsCollection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();

                var output = new Dictionary<string, (long TotalDurationMs, long IncidentCount)>();
                foreach (var rangeName in rangesInDays.Keys)
                {
                    var facetArray = result[rangeName].AsBsonArray;
                    var facetResult = facetArray.FirstOrDefault() as BsonDocument;

                    if (facetResult != null)
                    {
                        output[rangeName] = (
                            facetResult.GetValue("totalDurationMs", 0).ToInt64(),
                            facetResult.GetValue("incidentCount", 0).ToInt64()
                        );
                    }
                    else
                    {
                        output[rangeName] = (0, 0);
                    }
                }

                return output;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching downtime and incident counts for monitor {MonitorId}", monitorId);
                return rangesInDays.Keys.ToDictionary(k => k, k => (0L, 0L));
            }
        }

        public async Task<(List<MonitorIncident>, int)> GetIncidentsWithCountByMonitorIdAsync(MonitorConfiguration monitor, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = true)
        {
            try
            {
                var skip = pageNumber * pageSize;

                var matchStage = new BsonDocument("$match", new BsonDocument("MonitorId", monitor.ItemId));

                var facetStage = new BsonDocument("$facet", new BsonDocument
                {
                    {
                        "data",
                        new BsonArray
                        {
                            new BsonDocument("$sort", BuildSortBson(sortProperty, sortIsDescending)),
                            new BsonDocument("$skip", skip),
                            new BsonDocument("$limit", pageSize)
                        }
                    },
                    {
                        "count",
                        new BsonArray
                        {
                            new BsonDocument("$count", "TotalCount")
                        }
                    }
                });

                var pipeline = new[] { matchStage, facetStage };
                var result = await _monitorIncidentsCollection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();

                if (result == null)
                    return (new List<MonitorIncident>(), 0);

                var dataArray = result["data"].AsBsonArray;
                var data = dataArray
                    .Select(b => MongoDB.Bson.Serialization.BsonSerializer.Deserialize<MonitorIncident>(b.AsBsonDocument))
                    .ToList();

                var countArray = result["count"].AsBsonArray;
                var totalCount = countArray.Count > 0 ? (int)countArray.First()["TotalCount"].ToInt64() : 0;

                _logger.LogInformation(
                    "Fetched {Count} incidents out of {TotalCount} for MonitorId {MonitorId}",
                    data.Count, totalCount, monitor.ItemId
                );

                return (data, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching incidents for MonitorId {MonitorId}", monitor.ItemId);
                return (new List<MonitorIncident>(), 0);
            }
        }

        private static BsonDocument BuildSortBson(string? sortProperty, bool sortIsDescending)
        {
            var direction = sortIsDescending ? -1 : 1;
            return (sortProperty ?? string.Empty).ToLowerInvariant() switch
            {
                "status" => new BsonDocument("IsResolved", direction),
                "laststatuscode" => new BsonDocument("LastStatusCode", direction),
                "rootcause" => new BsonDocument("FailureReason", direction),
                "started_time" => new BsonDocument("StartTime", direction),
                "end_time" => new BsonDocument("EndTime", direction),
                "duration" => new BsonDocument("StartTime", direction),
                _ => new BsonDocument("StartTime", -1),
            };
        }

        public async Task<List<IncidentListSummary>> GetIncidentsListByDateRangeAsync(
            string monitorId, string? startDateStr, string? endDateStr)
        {
            try
            {
                var filterBuilder = Builders<MonitorIncident>.Filter;
                var filters = new List<FilterDefinition<MonitorIncident>>
                {
                    filterBuilder.Eq(x => x.MonitorId, monitorId)
                };

                if (DateTime.TryParse(startDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
                    startDate = startDate.ToUniversalTime();

                if (DateTime.TryParse(endDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
                    endDate = endDate.ToUniversalTime();
                else
                    endDate = DateTime.UtcNow;

                var overlapFilter = filterBuilder.And(
                    filterBuilder.Lte(i => i.StartTime, endDate),
                    filterBuilder.Or(
                        filterBuilder.Eq(i => i.EndTime, null),
                        filterBuilder.Gte(i => i.EndTime, startDate)
                    )
                );

                filters.Add(overlapFilter);

                var finalFilter = filterBuilder.And(filters);

                var partialResults = await _monitorIncidentsCollection
                    .Find(finalFilter)
                    .Project(x => new
                    {
                        x.StartTime,
                        x.EndTime
                    })
                    .ToListAsync();

                return partialResults.Select(x => new IncidentListSummary
                {
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    DowntimeDurationSeconds = x.EndTime.HasValue
                        ? (x.EndTime.Value - x.StartTime).TotalSeconds
                        : null
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching incident list by date range for MonitorId {MonitorId}", monitorId);
                return new List<IncidentListSummary>();
            }
        }

        public async Task<List<MonitorIncident>> GetIncidentsListByMonitorIdsAndDateRangeAsync(List<string> monitorIds, DateTime startDateUtc, DateTime endDateUtc)
        {
            if (monitorIds == null || monitorIds.Count == 0)
            {
                _logger.LogWarning("MonitorIds list is empty. Skipping incident query.");
                return new List<MonitorIncident>();
            }

            try
            {
                var filterBuilder = Builders<MonitorIncident>.Filter;
                var filter = filterBuilder.In(x => x.MonitorId, monitorIds) &
                                filterBuilder.Gte(x => x.StartTime, startDateUtc) &
                                filterBuilder.Lte(x => x.StartTime, endDateUtc);

                var incidents = await _monitorIncidentsCollection
                    .Find(filter)
                    .SortByDescending(x => x.StartTime)
                    .ToListAsync();

                _logger.LogInformation(
                    "Fetched {Count} incidents for {MonitorCount} monitors between {Start} and {End}",
                    incidents.Count, monitorIds.Count, startDateUtc, endDateUtc);

                return incidents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching incidents for multiple monitors between {Start} and {End}", startDateUtc, endDateUtc);
                return new List<MonitorIncident>();
            }
        }
    }
}
