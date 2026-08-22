using DomainService.Entities;

namespace DomainService.Configuration.Services
{
    public interface IConfigurationRepository
    {
        Task<NotificationConfiguration> GetByNameAsync(string name);
    }
}
