namespace Common.InternalService.Monitor
{
    public interface IMonitorPingService
    {
        Task<BaseApiResponse> GetPingLogsByDateRangeAsync(string monitorId, string? startDateStr, string? endDateStr);
    }
}
