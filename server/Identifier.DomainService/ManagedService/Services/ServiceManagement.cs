using Blocks.Genesis;
using DomainService.Shared;
using Microsoft.Extensions.Logging;

namespace DomainService.ManagedService.Services
{
    public class ServiceManagement : IServiceManagement
    {
        private readonly IServiceManagementRepository _serviceManagementRepository;
        private readonly IBlocksSecret _blocksSecret;
        private readonly ITenants _tenants;
        private readonly ILogger<ServiceManagement> _logger;

        public ServiceManagement(IServiceManagementRepository serviceManagementRepository,
                                 IBlocksSecret blocksSecret,
                                 ITenants tenants,
                                 ILogger<ServiceManagement> logger)
        {
            _serviceManagementRepository = serviceManagementRepository;
            _blocksSecret = blocksSecret;
            _tenants = tenants;
            _logger = logger;
        }

        public async Task<GetAllServiceResponse> GetAllServicesAsync(GetAllServiceRequest request)
        {
            try
            {
                var (data, count) = await _serviceManagementRepository.GetAllServicesAsync(request);
                var tenantId = BlocksContext.GetContext().TenantId;
                var tenant = _tenants.GetTenantByID(tenantId ?? string.Empty);

                var serviceList = data.ToList();

                foreach (var item in serviceList)
                {
                    item.ServiceBusConnectionString = EncryptionHelper.Decrypt(
                        item.ServiceBusConnectionString,
                        tenant?.TenantSalt ?? "LMT"
                    );
                    item.ServiceType = item.ServiceType ?? "backend";
                }

                return new GetAllServiceResponse
                {
                    Data = serviceList.AsQueryable(),
                    TotalCount = count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString(), "Error occurred while fetching services");
                throw;
            }
        }
    }
}