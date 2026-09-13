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

        /// <summary>
        /// Applies buffered traffic counters to the tenant's proxy documents as one unordered bulk write of
        /// <c>$inc</c> / <c>$max</c> operations, pruning hours that have fallen out of the retained window.
        /// <para>
        /// Every operation is additive and server-side, so batches from concurrent instances compose instead
        /// of racing, and no document is read first. A delta for a proxy that no longer exists matches
        /// nothing and is silently skipped — the update never upserts, so a deleted proxy cannot be
        /// resurrected as a counters-only document.
        /// </para>
        /// </summary>
        Task ApplyStatsDeltasAsync(
            string tenantId, IReadOnlyList<ProxyStatsDelta> deltas, CancellationToken cancellationToken = default);
    }
}
