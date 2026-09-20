using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Repositories
{
    /// <summary>Persistence for the append-only per-tenant <c>ProxyVersions</c> collection.</summary>
    public interface IProxyVersionRepository
    {
        Task InsertAsync(ProxyVersionEntity version);

        Task<ProxyVersionEntity?> GetAsync(string tenantId, string versionId);

        Task<List<ProxyVersionEntity>> GetForProxyAsync(string tenantId, string proxyId, int pageSize, int pageNumber);

        Task<long> CountForProxyAsync(string tenantId, string proxyId);
    }
}
