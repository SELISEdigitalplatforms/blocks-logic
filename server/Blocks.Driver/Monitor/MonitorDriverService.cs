using Common.InternalService.Monitor;

namespace Blocks.Driver.Monitor;

public class MonitorDriverService : IMonitorDriverService
{
    private readonly IMonitorObservabilityService _monitorObservabilityService;

    public MonitorDriverService(IMonitorObservabilityService monitorObservabilityService)
    {
        _monitorObservabilityService = monitorObservabilityService;
    }

    public Task<PaginatedResponse> GetMonitorListAsync(string projectKey, string? monitorSourceType, int pageNumber = 0, int pageSize = 10)
        => _monitorObservabilityService.GetMonitorListAsync(projectKey, monitorSourceType, pageNumber, pageSize);

    public Task<BaseApiResponse> GetMonitorListByRepoIdAsync(string projectKey, string repoId)
        => _monitorObservabilityService.GetMonitorListByRepoIdAsync(projectKey, repoId);

    public Task<BaseApiResponse> GetMonitorByIdAsync(string monitorId)
        => _monitorObservabilityService.GetMonitorByIdAsync(monitorId);

    public Task<BaseApiResponse> SaveMonitorAsync(SaveMonitorConfigurationRequest request)
        => _monitorObservabilityService.SaveMonitorAsync(request);

    public Task<BaseApiResponse> UpdateMonitorAsync(UpdateMonitorConfigurationRequest request)
        => _monitorObservabilityService.UpdateMonitorAsync(request);

    public Task<BaseApiResponse> DeleteMonitorAsync(string itemId)
        => _monitorObservabilityService.DeleteMonitorAsync(itemId);

    public Task<PaginatedResponse> GetIncidentListAsync(string monitorId, int pageNumber = 0, int pageSize = 10)
        => _monitorObservabilityService.GetIncidentListAsync(monitorId, pageNumber, pageSize);

    public Task<MonitorDetailsResponse> GetMonitorDetailsAsync(string monitorId)
        => _monitorObservabilityService.GetMonitorDetailsAsync(monitorId);

    public Task<BaseApiResponse> GetMonitorResponseTimeAsync(string monitorId, string? startDate, string? endDate)
        => _monitorObservabilityService.GetMonitorResponseTimeAsync(monitorId, startDate, endDate);

    public Task<BaseApiResponse> GetMonitorDownTimeAsync(string monitorId, string? startDate, string? endDate)
        => _monitorObservabilityService.GetMonitorDownTimeAsync(monitorId, startDate, endDate);

    public Task<BaseApiResponse> IsExternalServiceConfiguredAsync(string externalServiceId)
        => _monitorObservabilityService.IsExternalServiceConfiguredAsync(externalServiceId);
}
