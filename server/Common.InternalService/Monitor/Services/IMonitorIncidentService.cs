namespace Common.InternalService.Monitor
{
    public interface IMonitorIncidentService
    {
        Task<PaginatedResponse> GetIncidentsByMonitorIdAsync(string monitorId, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = true);
        Task<MonitorDetailsResponse> GetIncidentsDurationByDateRangeAsync(string monitorId);
        Task<BaseApiResponse> GetDownTimeLogsByDateRangeAsync(string monitorId, string? startDateStr, string? endDateStr);
    }
}
