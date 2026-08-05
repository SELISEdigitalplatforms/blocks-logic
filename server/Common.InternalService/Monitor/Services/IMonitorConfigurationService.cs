namespace Common.InternalService.Monitor
{
    public interface IMonitorConfigurationService
    {
        Task<BaseApiResponse> GetConfigurationByIdAsync(string monitorId);
        Task<PaginatedResponse> GetConfigurationListAsync(string tenantId, string? monitorSourceType, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = false);
        Task<BaseApiResponse> SaveConfigurationAsync(SaveMonitorConfigurationRequest request);
        Task<BaseApiResponse> UpdateConfigurationAsync(UpdateMonitorConfigurationRequest request);
        Task<BaseApiResponse> DeleteConfigurationAsync(string itemId);
        Task<BaseApiResponse> GetConfigurationListWithDowntimeByRepoIdAsync(string tenantId, string repoId);
    }
}
