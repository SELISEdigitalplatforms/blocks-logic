namespace Common.InternalService.Monitor
{
    public class MonitorObservabilityService : IMonitorObservabilityService
    {
        private readonly IMonitorConfigurationService _monitorConfigurationService;
        private readonly IMonitorConfigurationRepoService _monitorConfigurationRepoService;
        private readonly IMonitorIncidentService _monitorIncidentService;
        private readonly IMonitorPingService _monitorPingService;

        public MonitorObservabilityService(
            IMonitorConfigurationService monitorConfigurationService,
            IMonitorConfigurationRepoService monitorConfigurationRepoService,
            IMonitorIncidentService monitorIncidentService,
            IMonitorPingService monitorPingService)
        {
            _monitorConfigurationService = monitorConfigurationService;
            _monitorConfigurationRepoService = monitorConfigurationRepoService;
            _monitorIncidentService = monitorIncidentService;
            _monitorPingService = monitorPingService;
        }

        public Task<PaginatedResponse> GetMonitorListAsync(
            string projectKey,
            string? monitorSourceType,
            int pageNumber = 0,
            int pageSize = 10)
            => _monitorConfigurationService.GetConfigurationListAsync(projectKey, monitorSourceType, pageNumber, pageSize);

        public Task<BaseApiResponse> GetMonitorListByRepoIdAsync(string projectKey, string repoId)
            => _monitorConfigurationService.GetConfigurationListWithDowntimeByRepoIdAsync(projectKey, repoId);

        public Task<BaseApiResponse> GetMonitorByIdAsync(string monitorId)
            => _monitorConfigurationService.GetConfigurationByIdAsync(monitorId);

        public Task<BaseApiResponse> SaveMonitorAsync(SaveMonitorConfigurationRequest request)
            => _monitorConfigurationService.SaveConfigurationAsync(request);

        public Task<BaseApiResponse> UpdateMonitorAsync(UpdateMonitorConfigurationRequest request)
            => _monitorConfigurationService.UpdateConfigurationAsync(request);

        public Task<BaseApiResponse> DeleteMonitorAsync(string itemId)
            => _monitorConfigurationService.DeleteConfigurationAsync(itemId);

        public Task<PaginatedResponse> GetIncidentListAsync(string monitorId, int pageNumber = 0, int pageSize = 10)
            => _monitorIncidentService.GetIncidentsByMonitorIdAsync(monitorId, pageNumber, pageSize);

        public Task<MonitorDetailsResponse> GetMonitorDetailsAsync(string monitorId)
            => _monitorIncidentService.GetIncidentsDurationByDateRangeAsync(monitorId);

        public Task<BaseApiResponse> GetMonitorResponseTimeAsync(string monitorId, string? startDate, string? endDate)
            => _monitorPingService.GetPingLogsByDateRangeAsync(monitorId, startDate, endDate);

        public Task<BaseApiResponse> GetMonitorDownTimeAsync(string monitorId, string? startDate, string? endDate)
            => _monitorIncidentService.GetDownTimeLogsByDateRangeAsync(monitorId, startDate, endDate);

        public async Task<BaseApiResponse> IsExternalServiceConfiguredAsync(string externalServiceId)
        {
            var result = await _monitorConfigurationRepoService.GetExternalServiceConfigurationAsync(externalServiceId);
            return new BaseApiResponse
            {
                Data = result!,
                IsSuccess = true
            };
        }
    }
}
