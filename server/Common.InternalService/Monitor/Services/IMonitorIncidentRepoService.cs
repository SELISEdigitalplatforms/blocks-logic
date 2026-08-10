namespace Common.InternalService.Monitor
{
    public interface IMonitorIncidentRepoService
    {
        Task<List<MonitorIncident>> GetIncidentsByMonitorIdAsync(MonitorConfiguration monitor, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = true);
        Task<List<IncidentListSummary>> GetIncidentsListByDateRangeAsync(string monitorId, string? startDateStr, string? endDateStr);
        Task<(List<MonitorIncident>, int)> GetIncidentsWithCountByMonitorIdAsync(MonitorConfiguration monitor, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = true);
        Task<Dictionary<string, (long TotalDurationMs, long IncidentCount)>> GetDowntimeAndCountByDateRangesAsync(string monitorId, Dictionary<string, int> rangesInDays);
        Task<List<MonitorIncident>> GetIncidentsListByMonitorIdsAndDateRangeAsync(List<string> monitorIds, DateTime startDateUtc, DateTime endDateUtc);
    }
}
