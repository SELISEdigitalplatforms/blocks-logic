using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Azure.ResourceManager;
using Azure.ResourceManager.ServiceBus;
using Blocks.Genesis;
using DomainService.Shared;
using DomainService.Shared.Entities;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SeliseBlocks.LMT.Client;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DomainService.ManagedService.Services
{
    public class ServiceManagement : IServiceManagement
    {
        private const string FallbackEncryptionKey = "LMT";

        private readonly ILogger<ServiceManagement> _logger;
        private readonly IServiceManagementRepository _serviceManagementRepository;
        private readonly ITenants _tenants;
        private readonly IConfiguration _configuration;

        [ExcludeFromCodeCoverage]
        public ServiceManagement ( ILogger<ServiceManagement> logger,
                                 IServiceManagementRepository serviceManagementRepository,
                                 IBlocksSecret blocksSecret,
                                 ICacheClient cacheClient,
                                 ITenants tenants,
                                 IConfiguration configuration )
        {
            _logger = logger;
            _serviceManagementRepository = serviceManagementRepository;
            _tenants = tenants;
            _configuration = configuration;
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

            // Root can hold rows of one TenantId with two salts (project salt and console/root salt). Step 1 is
            // today's rule. Step 2 = the context tenant's own salt (matters when impersonating, where step 1 used
            // the console's salt), then the root tenant's salt. Salts equal to step 1's are skipped.
            var firstSalt = tenant?.TenantSalt ?? FallbackEncryptionKey;
            var extraSalts = new List<string>();
            var contextTenantId = BlocksContext.GetContext()?.TenantId;
            if (!string.IsNullOrEmpty(contextTenantId))
            {
                extraSalts.Add(_tenants.GetTenantByID(contextTenantId)?.TenantSalt ?? string.Empty);
            }
            var rootTenantId = _configuration?["RootTenantId"];
            if (!string.IsNullOrEmpty(rootTenantId))
            {
                extraSalts.Add(_tenants.GetTenantByID(rootTenantId)?.TenantSalt ?? string.Empty);
            }
            extraSalts = extraSalts.Where(x => !string.IsNullOrEmpty(x) && x != firstSalt).Distinct().ToList();

            foreach (var item in serviceList)
            {
                var plainText = DecryptConnectionString(
                    item.ServiceBusConnectionString,
                    firstSalt
                );

                if (plainText.Length == 0 && !string.IsNullOrEmpty(item.ServiceBusConnectionString))
                {
                    foreach (var extraSalt in extraSalts)
                    {
                        plainText = DecryptConnectionString(item.ServiceBusConnectionString, extraSalt);
                        if (plainText.Length > 0) break;
                    }
                }

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
