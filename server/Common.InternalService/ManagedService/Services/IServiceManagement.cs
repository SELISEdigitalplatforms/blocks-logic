using DomainService.ManagedService.Services;

namespace DomainService.ManagedService
{
    public interface IServiceManagement
    {
        Task<GetAllServiceResponse> GetAllServicesAsync(GetAllServiceRequest request);
    }
}