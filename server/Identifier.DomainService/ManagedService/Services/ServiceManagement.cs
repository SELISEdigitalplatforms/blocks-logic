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
        private readonly IServiceBusConnectionStringResolver _connectionStringResolver;

        [ExcludeFromCodeCoverage]
        public ServiceManagement ( ILogger<ServiceManagement> logger,
                                 IServiceManagementRepository serviceManagementRepository,
                                 IBlocksSecret blocksSecret,
                                 ICacheClient cacheClient,
                                 ITenants tenants,
                                 IConfiguration configuration,
                                 IServiceBusConnectionStringResolver connectionStringResolver )
        {
            _logger = logger;
            _serviceManagementRepository = serviceManagementRepository;
            _tenants = tenants;
            _configuration = configuration;
            _connectionStringResolver = connectionStringResolver;
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

            var contextTenantId = BlocksContext.GetContext()?.TenantId;
            var firstSalt = tenant?.TenantSalt ?? FallbackEncryptionKey;
            var keys = new LegacyKeys(
                tenantId,
                firstSalt,
                tenant is null ? "fallback" : "tenant salt",
                ExtraSalts(firstSalt, contextTenantId) );

            foreach (var item in serviceList)
            {
                await ResolveConnectionStringAsync(item, contextTenantId, keys);
            }

            return new GetAllServiceResponse
            {
                Data = serviceList.AsQueryable(),
                TotalCount = count
            };
        }

        /// <summary>
        /// The salts the legacy ladder tries for one page: <see cref="FirstSalt"/> (today's rule),
        /// then <see cref="ExtraSalts"/>.
        /// </summary>
        private sealed record LegacyKeys ( string TenantId, string FirstSalt, string KeySource, IReadOnlyList<string> ExtraSalts );

        /// <summary>
        /// Root can hold rows of one TenantId with two salts (project salt and console/root salt). Step 1 is
        /// today's rule. Step 2 = the context tenant's own salt (matters when impersonating, where step 1 used
        /// the console's salt), then the root tenant's salt. Salts equal to step 1's are skipped.
        /// </summary>
        private List<string> ExtraSalts ( string firstSalt, string? contextTenantId )
        {
            var extraSalts = new List<string>();
            if (!string.IsNullOrEmpty(contextTenantId))
            {
                extraSalts.Add(_tenants.GetTenantByID(contextTenantId)?.TenantSalt ?? string.Empty);
            }
            var rootTenantId = _configuration?["RootTenantId"];
            if (!string.IsNullOrEmpty(rootTenantId))
            {
                extraSalts.Add(_tenants.GetTenantByID(rootTenantId)?.TenantSalt ?? string.Empty);
            }
            return extraSalts.Where(x => !string.IsNullOrEmpty(x) && x != firstSalt).Distinct().ToList();
        }

        /// <summary>
        /// Puts the plaintext connection string of one row in place and clears its reference, so
        /// the reference never leaves this service.
        /// </summary>
        private async Task ResolveConnectionStringAsync ( BlocksManagedService item, string? contextTenantId, LegacyKeys keys )
        {
            item.ServiceType ??= "backend";

            // A non-empty reference is authoritative: the value is in Blocks Secrets and the
            // ciphertext field is not used, even when it is non-empty.
            item.ServiceBusConnectionString = string.IsNullOrEmpty(item.ServiceBusConnectionStringReference)
                ? DecryptLegacyConnectionString(item, keys)
                : await ReadReferencedConnectionStringAsync(item, contextTenantId);

            item.ServiceBusConnectionStringReference = null;
        }

        /// <summary>Today's salt ladder for a row without a reference; empty when no salt fits.</summary>
        private string DecryptLegacyConnectionString ( BlocksManagedService item, LegacyKeys keys )
        {
            var cipherText = item.ServiceBusConnectionString;
            var plainText = DecryptConnectionString(cipherText, keys.FirstSalt);
            if (plainText.Length > 0 || string.IsNullOrEmpty(cipherText))
            {
                return plainText;
            }

            foreach (var extraSalt in keys.ExtraSalts)
            {
                plainText = DecryptConnectionString(cipherText, extraSalt);
                if (plainText.Length > 0)
                {
                    return plainText;
                }
            }

            _logger.LogError(
                "Connection string for service {ServiceId} (tenant {TenantId}) could not be decrypted with " +
                "the {KeySource} key; returning it empty",
                item.ServiceId, keys.TenantId, keys.KeySource);
            return plainText;
        }

        /// <summary>
        /// One secret read for one row, in the row's tenant (the context tenant when the row has
        /// none). Under impersonation that is the project tenant, where blocks-os writes the secret.
        /// Any failure blanks only this row; the log carries the service id and the exception type,
        /// never the reference id or the exception message (which carries the reference id).
        /// </summary>
        private async Task<string> ReadReferencedConnectionStringAsync ( BlocksManagedService item, string? contextTenantId )
        {
            var secretTenantId = string.IsNullOrEmpty(item.TenantId) ? contextTenantId : item.TenantId;
            string failureType;

            if (string.IsNullOrWhiteSpace(secretTenantId))
            {
                // No tenant to read the connection string in: no read, and the same log line as
                // any other failure.
                failureType = nameof(InvalidOperationException);
            }
            else
            {
                try
                {
                    return await _connectionStringResolver.ResolveAsync(secretTenantId, item.ServiceBusConnectionStringReference!);
                }
                catch (Exception ex)
                {
                    // Only the type name leaves the catch: the exception object is deliberately not
                    // logged, because its message and properties carry the reference id.
                    failureType = ex.GetType().Name;
                }
            }

            _logger.LogError(
                "Connection string for service {ServiceId} could not be read from Blocks Secrets ({ExceptionType}); returning it empty",
                item.ServiceId, failureType);
            return string.Empty;
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
