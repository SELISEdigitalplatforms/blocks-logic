using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Common.InternalService.Monitor
{
    public class MonitorPingService : IMonitorPingService
    {
        private readonly IMonitorConfigurationRepoService _monitorConfigurationRepoService;
        private readonly ILogger<MonitorPingService> _logger;
        private readonly IMonitorPingRepoService _monitorPingRepoService;

        public MonitorPingService(
            IMonitorConfigurationRepoService monitorConfigurationRepoService,
            IMonitorPingRepoService monitorPingRepoService,
            ILogger<MonitorPingService> logger)
        {
            _monitorConfigurationRepoService = monitorConfigurationRepoService;
            _logger = logger;
            _monitorPingRepoService = monitorPingRepoService;
        }

        private static BaseApiResponse FailResponse(string message) =>
            new BaseApiResponse { IsSuccess = false, Message = message };

        private static BaseApiResponse OkResponse(string message, object data = null) =>
            new BaseApiResponse { IsSuccess = true, Message = message, Data = data };

        public async Task<BaseApiResponse> GetPingLogsByDateRangeAsync(string monitorId, string? startDateStr, string? endDateStr)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(monitorId))
                {
                    _logger.LogWarning("MonitorId is null or empty while fetching ping logs.");
                    return FailResponse("MonitorId cannot be null or empty.");
                }

                DateTime endDateUtc = DateTime.UtcNow;
                DateTime startDateUtc = endDateUtc.AddHours(-1);

                if (!string.IsNullOrWhiteSpace(startDateStr) && DateTime.TryParse(startDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedStart))
                    startDateUtc = parsedStart.ToUniversalTime();

                if (!string.IsNullOrWhiteSpace(endDateStr) && DateTime.TryParse(endDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd))
                    endDateUtc = parsedEnd.ToUniversalTime();

                if (endDateUtc < startDateUtc)
                {
                    _logger.LogWarning("End date is earlier than start date. MonitorId: {MonitorId}", monitorId);
                    return FailResponse("End date cannot be earlier than start date.");
                }

                var monitorConfig = await _monitorConfigurationRepoService.GetConfigurationAsync(monitorId);
                if (monitorConfig == null)
                {
                    _logger.LogWarning("Monitor configuration not found for MonitorId {MonitorId}", monitorId);
                    return FailResponse($"Monitor configuration not found for MonitorId: {monitorId}");
                }

                var logs = await _monitorPingRepoService.GetPingLogsByDateRangeAsync(
                    monitorId,
                    startDateUtc.ToString("O"),
                    endDateUtc.ToString("O")
                );

                if (logs == null || logs.Count == 0)
                {
                    _logger.LogInformation("No ping logs found for MonitorId {MonitorId} in range {Start} - {End}", monitorId, startDateUtc, endDateUtc);
                    return OkResponse("No ping logs found for the given date range.", new List<MonitorPingLogSummary>());
                }

                _logger.LogInformation("Fetched {Count} ping logs for MonitorId {MonitorId} between {Start} and {End}", logs.Count, monitorId, startDateUtc, endDateUtc);

                return OkResponse("Ping logs retrieved successfully.", logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ping logs for MonitorId {MonitorId} between {StartDate} and {EndDate}", monitorId, startDateStr, endDateStr);
                return FailResponse($"An error occurred while fetching ping logs: {ex.Message}");
            }
        }
    }
}
