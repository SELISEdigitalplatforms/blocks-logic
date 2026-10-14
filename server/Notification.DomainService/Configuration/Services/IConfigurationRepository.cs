using Blocks.Genesis;
using Blocks.Genesis.Entities.Os;

namespace DomainService.Configuration.Services
{
    public interface IConfigurationRepository
    {
        Task SaveAsync(NotificationConfiguration configuration);
        Task<NotificationConfiguration> GetByNameAsync(string name);
        Task<NotificationConfiguration> GetByIdAsync(string id);
        Task<GetConfigurationsResponse> GetConfigurationsAsync(GetConfigurationsRequest request);
        Task<BaseResponse> DeleteConfigurationAsync(DeleteConfigurationRequest request);
    }
}
