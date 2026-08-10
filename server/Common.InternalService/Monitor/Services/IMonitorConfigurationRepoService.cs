namespace Common.InternalService.Monitor
{
    public interface IMonitorConfigurationRepoService
    {
        Task<MonitorConfiguration?> GetConfigurationAsync(string itemId);
        Task<(List<MonitorConfiguration> Items, int TotalCount)> GetConfigurationListAsync(string tenantId, string? monitorSourceType, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = false);
        Task<List<MonitorConfiguration>> GetConfigurationListByTenantIdAsync(string tenantId);
        Task<List<MonitorConfiguration>> GetConfigurationListByRepoIdAsync(string tenantId, string repoId);
        Task<bool> SaveConfigurationAsync(MonitorConfiguration monitorConfiguration);
        Task<bool> DeleteConfigurationAsync(string itemId);
        Task<MonitorConfiguration> GetByUrlAsync(string url);
        Task<MonitorConfiguration?> GetExternalServiceConfigurationAsync(string externalServiceId);
    }
}
