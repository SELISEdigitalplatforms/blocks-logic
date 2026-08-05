using System.Globalization;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Common.InternalService.Monitor
{
    public class MonitorPingRepoService : IMonitorPingRepoService
    {
        private readonly ILogger<MonitorPingRepoService> _logger;
        private readonly IMongoCollection<MonitorPingLog> _monitorPingLogsCollection;

        public MonitorPingRepoService(
            ILogger<MonitorPingRepoService> logger,
            IDbContextProvider dbContextProvider,
            IBlocksSecret blocksSecret)
        {
            var db = dbContextProvider.GetDatabase(blocksSecret.DatabaseConnectionString, blocksSecret.RootDatabaseName);

            _logger = logger;
            _monitorPingLogsCollection = db.GetCollection<MonitorPingLog>("MonitorPingLogs");
        }

        public async Task<List<MonitorPingLogSummary>> GetPingLogsByDateRangeAsync(string monitorId, string startDateStr, string endDateStr)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(monitorId))
                {
                    _logger.LogWarning("MonitorId is null or empty while fetching ping logs.");
                    return new List<MonitorPingLogSummary>();
                }

                if (!DateTime.TryParse(startDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate) ||
                    !DateTime.TryParse(endDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
                {
                    _logger.LogWarning("Invalid date format received. Start: {StartDate}, End: {EndDate}", startDateStr, endDateStr);
                    return new List<MonitorPingLogSummary>();
                }

                startDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
                endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

                var filterBuilder = Builders<MonitorPingLog>.Filter;
                var filter = filterBuilder.Eq(x => x.MonitorId, monitorId) &
                             filterBuilder.Gte(x => x.Timestamp, startDate) &
                             filterBuilder.Lte(x => x.Timestamp, endDate);

                var projection = Builders<MonitorPingLog>.Projection
                    .Include(x => x.MonitorId)
                    .Include(x => x.Timestamp)
                    .Include(x => x.ResponseTimeMs)
                    .Include(x => x.StatusCode);

                var results = await _monitorPingLogsCollection
                    .Find(filter)
                    .Project<MonitorPingLogSummary>(projection)
                    .ToListAsync();

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ping logs for MonitorId {MonitorId} between {StartDate} and {EndDate}", monitorId, startDateStr, endDateStr);
                return new List<MonitorPingLogSummary>();
            }
        }
    }
}
