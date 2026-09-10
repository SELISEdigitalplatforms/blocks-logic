using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Repositories
{
    /// <summary>Persistence for the per-tenant <c>Proxies</c> collection.</summary>
    public interface IProxyRepository
    {
        Task<ProxyDetailEntity?> GetAsync(string tenantId, string itemId);

        Task<ProxyDetailEntity?> GetBySlugAsync(string tenantId, string slug);

        Task<List<ProxyDetailEntity>> GetAllAsync(string tenantId, string? search, bool? enabled, int pageSize, int pageNumber);

        Task<long> CountAsync(string tenantId, string? search, bool? enabled);

        Task InsertAsync(ProxyDetailEntity proxy);

        Task ReplaceAsync(ProxyDetailEntity proxy);

        Task DeleteAsync(string tenantId, string itemId);
    }
}
