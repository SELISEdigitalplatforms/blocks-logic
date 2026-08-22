using Blocks.Genesis;
using DomainService.Shared;
using DomainService.Shared.Entities;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.ManagedService.Services
{
    public class ServiceManagement : IServiceManagement
    {
        private const string FallbackEncryptionKey = "LMT";

        private readonly ILogger<ServiceManagement> _logger;
        private readonly IServiceManagementRepository _serviceManagementRepository;
        private readonly ITenants _tenants;
       

        [ExcludeFromCodeCoverage]
        public ServiceManagement ( ILogger<ServiceManagement> logger,
                                 IServiceManagementRepository serviceManagementRepository,
                                 ITenants tenants )
        {
            _logger = logger;
            _serviceManagementRepository = serviceManagementRepository;
            _tenants = tenants;
        }
        public async Task<GetAllServiceResponse> GetAllServicesAsync ( GetAllServiceRequest request )
        {
            var (data, count) = await _serviceManagementRepository.GetAllServicesAsync(request);

            var tenantId = ResolveEncryptionTenantId();
            var tenant = _tenants.GetTenantByID(tenantId);

            var serviceList = data.ToList();

            if (tenant is null && serviceList.Count > 0)
            {
                _logger.LogWarning(
                    "Tenant {TenantId} did not resolve; decrypting {Count} service(s) with the fallback key",
                    tenantId, serviceList.Count);
            }

            foreach (var item in serviceList)
            {
                var plainText = DecryptConnectionString(
                    item.ServiceBusConnectionString,
                    tenant?.TenantSalt ?? FallbackEncryptionKey
                );

                if (plainText.Length == 0 && !string.IsNullOrEmpty(item.ServiceBusConnectionString))
                {
                    _logger.LogError(
                        "Connection string for service {ServiceId} (tenant {TenantId}) could not be decrypted with " +
                        "the {KeySource} key; returning it empty",
                        item.ServiceId, tenantId, tenant is null ? "fallback" : "tenant salt");
                }

                item.ServiceBusConnectionString = plainText;
                item.ServiceType = item.ServiceType ?? "backend";
            }

            return new GetAllServiceResponse
            {
                Data = serviceList.AsQueryable(),
                TotalCount = count
            };
        }

        
        private static string ResolveEncryptionTenantId ( )
        {
            var context = BlocksContext.GetContext();

            if (context is null)
            {
                return string.Empty;
            }

            return context.Impersonated && !string.IsNullOrEmpty(context.OriginalTenantId)
                ? context.OriginalTenantId
                : context.TenantId;
        }

       
        private static string DecryptConnectionString ( string cipherText, string salt )
        {
            if (EncryptionHelper.TryDecrypt(cipherText, salt, out var plainText))
            {
                return plainText;
            }

            if (salt != FallbackEncryptionKey &&
                EncryptionHelper.TryDecrypt(cipherText, FallbackEncryptionKey, out plainText))
            {
                return plainText;
            }

            return string.Empty;
        }
    }
}
