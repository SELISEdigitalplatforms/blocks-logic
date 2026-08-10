namespace Common.InternalService.Monitor
{
    public interface IMonitorPingRepoService
    {
        Task<List<MonitorPingLogSummary>> GetPingLogsByDateRangeAsync(string monitorId, string startDateStr, string endDateStr);
    }
}
