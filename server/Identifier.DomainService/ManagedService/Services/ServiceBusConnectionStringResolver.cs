using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DomainService.ManagedService.Services
{
    public interface IServiceBusConnectionStringResolver
    {
        /// <summary>
        /// Reads the connection string behind <paramref name="reference"/> in
        /// <paramref name="tenantId"/>. Throws on any failure; the caller blanks the row.
        /// </summary>
        Task<string> ResolveAsync(string tenantId, string reference, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reads a service's connection string from Blocks Secrets when its row holds a
    /// <c>ServiceBusConnectionStringReference</c>.
    /// </summary>
    /// <remarks>
    /// A singleton with no per-call state: <see cref="TenantSecretReader"/> opens a DI scope per
    /// read for the scoped ISecretService, enters an authenticated context for the row's tenant and
    /// restores the caller's context afterwards. Nothing is cached.
    /// </remarks>
    public sealed class ServiceBusConnectionStringResolver : IServiceBusConnectionStringResolver
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ServiceBusConnectionStringResolver(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public Task<string> ResolveAsync(string tenantId, string reference, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
            ArgumentException.ThrowIfNullOrEmpty(reference);

            return TenantSecretReader.ReadAsync(_scopeFactory, tenantId, reference, cancellationToken);
        }
    }
}
