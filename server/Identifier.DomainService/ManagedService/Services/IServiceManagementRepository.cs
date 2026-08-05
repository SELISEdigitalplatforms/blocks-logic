using DomainService.Shared.Entities;

namespace DomainService.ManagedService.Services
{
    public interface IServiceManagementRepository
    {
        Task<(IQueryable<BlocksManagedService>, long)> GetAllServicesAsync(GetAllServiceRequest request);
    }
}
