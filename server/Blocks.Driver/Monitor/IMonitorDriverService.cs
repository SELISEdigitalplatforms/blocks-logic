using Common.InternalService.Monitor;

namespace Blocks.Driver.Monitor;

/// <summary>
/// Service for querying and managing monitors, their incidents and their observability data.
/// </summary>
public interface IMonitorDriverService
{
    Task<PaginatedResponse> GetMonitorListAsync(string projectKey, string? monitorSourceType, int pageNumber = 0, int pageSize = 10);
    Task<BaseApiResponse> GetMonitorListByRepoIdAsync(string projectKey, string repoId);
    Task<BaseApiResponse> GetMonitorByIdAsync(string monitorId);
    Task<BaseApiResponse> SaveMonitorAsync(SaveMonitorConfigurationRequest request);
    Task<BaseApiResponse> UpdateMonitorAsync(UpdateMonitorConfigurationRequest request);
    Task<BaseApiResponse> DeleteMonitorAsync(string itemId);
    Task<PaginatedResponse> GetIncidentListAsync(string monitorId, int pageNumber = 0, int pageSize = 10);
    Task<MonitorDetailsResponse> GetMonitorDetailsAsync(string monitorId);
    Task<BaseApiResponse> GetMonitorResponseTimeAsync(string monitorId, string? startDate, string? endDate);
    Task<BaseApiResponse> GetMonitorDownTimeAsync(string monitorId, string? startDate, string? endDate);
    Task<BaseApiResponse> IsExternalServiceConfiguredAsync(string externalServiceId);
}
