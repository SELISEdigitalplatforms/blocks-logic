using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Common.InternalService.Monitor
{
    public class MonitorIncidentService : IMonitorIncidentService
    {
        private readonly IMonitorIncidentRepoService _monitorIncidentRepoService;
        private readonly IMonitorConfigurationRepoService _monitorConfigurationRepoService;
        private readonly ILogger<MonitorIncidentService> _logger;

        public MonitorIncidentService(
            IMonitorIncidentRepoService monitorIncidentRepoService,
            IMonitorConfigurationRepoService monitorConfigurationRepoService,
            ILogger<MonitorIncidentService> logger)
        {
            _monitorIncidentRepoService = monitorIncidentRepoService;
            _logger = logger;
            _monitorConfigurationRepoService = monitorConfigurationRepoService;
        }

        public async Task<PaginatedResponse> GetIncidentsByMonitorIdAsync(string monitorId, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = true)
        {
            try
            {
                if (pageNumber < 0) pageNumber = 0;
                if (pageSize < 1) pageSize = 10;

                MonitorConfiguration monitorConfiguration = await _monitorConfigurationRepoService.GetConfigurationAsync(monitorId);
                if (monitorConfiguration is null)
                {
                    _logger.LogWarning("Monitor configuration not found for MonitorId {MonitorId}", monitorId);
                    return new PaginatedResponse
                    {
                        Data = new List<MonitorIncident>(),
                        TotalCount = 0,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                }
                var (result, totalCount) = await _monitorIncidentRepoService.GetIncidentsWithCountByMonitorIdAsync(monitorConfiguration, pageNumber, pageSize, sortProperty, sortIsDescending);
                return new PaginatedResponse
                {
                    Data = result,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Message = result.Count == 0 ? $"No incidents found for monitor {monitorId}" : ""
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paginated incidents for monitor {MonitorId}", monitorId);
                return new PaginatedResponse
                {
                    Data = new List<MonitorIncident>(),
                    TotalCount = 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
        }

        public async Task<MonitorDetailsResponse> GetIncidentsDurationByDateRangeAsync(string monitorId)
        {
            var response = new MonitorDetailsResponse();

            try
            {
                if (string.IsNullOrWhiteSpace(monitorId))
                {
                    _logger.LogWarning("MonitorId is null or empty while fetching incident durations.");

                    response.IsSuccess = false;
                    response.Message = "Invalid MonitorId.";
                    return response;
                }

                var monitorConfig = await _monitorConfigurationRepoService.GetConfigurationAsync(monitorId);
                if (monitorConfig == null)
                {
                    _logger.LogWarning("Monitor configuration not found for MonitorId {MonitorId}", monitorId);

                    response.IsSuccess = false;
                    response.Message = $"Monitor configuration not found for MonitorId {monitorId}";
                    return response;
                }

                var ranges = new Dictionary<string, int>
                {
                    { "7d", 7 },
                    { "30d", 30 },
                    { "365d", 365 }
                };

                var results = await _monitorIncidentRepoService.GetDowntimeAndCountByDateRangesAsync(monitorId, ranges);

                var dateRangeSummaryList = results.Select(kv => new MonitorDateRangeSummaryResponse
                {
                    Range = kv.Key,
                    TotalDurationMs = kv.Value.TotalDurationMs,
                    IncidentCount = kv.Value.IncidentCount
                }).ToList();

                var recentPingLogs = await _monitorIncidentRepoService.GetIncidentsByMonitorIdAsync(monitorConfig, 1, 5);

                response.IsSuccess = true;
                response.Message = "Incident summary fetched successfully.";
                response.DateRangeSummary = dateRangeSummaryList;
                response.MonitorIncidents = recentPingLogs;

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching incidents duration by date range for MonitorId {MonitorId}", monitorId);

                response.IsSuccess = false;
                response.Message = "An error occurred while fetching incident data.";
                return response;
            }
        }

        public async Task<BaseApiResponse> GetDownTimeLogsByDateRangeAsync(string monitorId, string? startDateStr, string? endDateStr)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(monitorId))
                {
                    _logger.LogWarning("MonitorId is null or empty while fetching ping logs.");
                    return new BaseApiResponse
                    {
                        IsSuccess = false,
                        Message = "MonitorId cannot be null or empty."
                    };
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
                    return new BaseApiResponse
                    {
                        IsSuccess = false,
                        Message = "End date cannot be earlier than start date."
                    };
                }

                var monitorConfig = await _monitorConfigurationRepoService.GetConfigurationAsync(monitorId);
                if (monitorConfig == null)
                {
                    _logger.LogWarning("Monitor configuration not found for MonitorId {MonitorId}", monitorId);
                    return new BaseApiResponse
                    {
                        IsSuccess = false,
                        Message = $"Monitor configuration not found for MonitorId: {monitorId}"
                    };
                }

                var incidents = await _monitorIncidentRepoService.GetIncidentsListByDateRangeAsync(monitorId, startDateStr, endDateStr);

                if (incidents == null || incidents.Count == 0)
                {
                    _logger.LogInformation("No ping logs found for MonitorId {MonitorId} in range {Start} - {End}", monitorId, startDateUtc, endDateUtc);
                    return new BaseApiResponse
                    {
                        IsSuccess = true,
                        Message = "No ping logs found for the given date range.",
                        Data = new List<MonitorPingLogSummary>()
                    };
                }

                _logger.LogInformation("Fetched {Count} ping logs for MonitorId {MonitorId} between {Start} and {End}", incidents.Count, monitorId, startDateUtc, endDateUtc);

                return new BaseApiResponse
                {
                    IsSuccess = true,
                    Message = "Ping logs retrieved successfully.",
                    Data = incidents
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ping logs for MonitorId {MonitorId} between {StartDate} and {EndDate}", monitorId, startDateStr, endDateStr);
                return new BaseApiResponse
                {
                    IsSuccess = false,
                    Message = $"An error occurred while fetching ping logs: {ex.Message}"
                };
            }
        }
    }
}
