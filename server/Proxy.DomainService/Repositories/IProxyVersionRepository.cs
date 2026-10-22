using Proxy.DomainService.Entities;
using Blocks.Genesis.Entities.Logic;

namespace Proxy.DomainService.Repositories
{
    /// <summary>Persistence for the append-only per-tenant <c>ProxyVersions</c> collection.</summary>
    public interface IProxyVersionRepository
    {
        Task InsertAsync(ProxyVersionEntity version);

        Task<ProxyVersionEntity?> GetAsync(string tenantId, string versionId);

        Task<List<ProxyVersionEntity>> GetForProxyAsync(string tenantId, string proxyId, int pageSize, int pageNumber);
        /// <summary>As the caller may see them: properties a View column rule hides are cleared. Never save them back.</summary>
        Task<List<ProxyVersionEntity>> GetForProxyForViewAsync(string tenantId, string proxyId, int pageSize, int pageNumber);

        Task<long> CountForProxyAsync(string tenantId, string proxyId);
    }
}
