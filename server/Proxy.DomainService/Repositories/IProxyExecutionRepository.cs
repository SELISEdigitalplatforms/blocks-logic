using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Repositories
{
    /// <summary>
    /// Persistence for the per-tenant <c>ProxyExecutions</c> collection. Phase 2 writes rows; Phase 3 adds the
    /// read side for the <em>Request logs</em> tab, the Overview tiles, the CSV export, and the
    /// <c>GetAll.calls24h</c> per-card count. Every read is scoped to one tenant and, except
    /// <see cref="CountByProxyAsync"/>, one proxy, and bounded to a caller-supplied 24 h window.
    /// </summary>
    public interface IProxyExecutionRepository
    {
        /// <summary>Inserts one execution row. Throws on a persistence failure so the caller can log it (C9).</summary>
        Task InsertAsync(ProxyExecutionEntity execution);

        /// <summary>
        /// One page of the proxy's executions with <c>sinceUtc &lt;= StartedAtUtc &lt;= asOfUtc</c> matching
        /// <paramref name="statusClass"/>, newest-first (<c>StartedAtUtc</c> desc, then <c>ItemId</c> desc).
        /// <para>
        /// <paramref name="asOfUtc"/> freezes the top of the window for the duration of a paging session.
        /// Without it, rows arriving between page 1 and page 2 shift every later row down by their count, so
        /// the reader sees rows repeat and rows vanish — the standard failure of offset paging over a
        /// collection that is appended to at the head.
        /// </para>
        /// </summary>
        Task<List<ProxyExecutionEntity>> GetPageAsync(
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc, DateTime asOfUtc,
            int pageSize, int pageNumber);

        /// <summary>
        /// Up to <paramref name="pageSize"/> rows STRICTLY NEWER than <c>(afterStartedAtUtc, afterItemId)</c>
        /// within the window and status class, newest-first. Used by Live mode (H3).
        /// </summary>
        Task<List<ProxyExecutionEntity>> GetNewerThanAsync(
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc,
            DateTime afterStartedAtUtc, string afterItemId, int pageSize);

        /// <summary>
        /// Unpaged count of the proxy's rows in the window matching <paramref name="statusClass"/>, bounded
        /// above by <paramref name="asOfUtc"/> so the total agrees with what <see cref="GetPageAsync"/> pages
        /// through.
        /// </summary>
        Task<long> CountAsync(
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc, DateTime asOfUtc);

        /// <summary>The row for <c>(proxyId, itemId)</c> in this tenant, or <c>null</c> (SPEC &sect;3.2 / C3).</summary>
        Task<ProxyExecutionEntity?> GetByIdAsync(string tenantId, string proxyId, string itemId);

        /// <summary>
        /// The row for <paramref name="itemId"/> in this tenant regardless of proxy, or <c>null</c>. Used to
        /// resolve the <c>afterId</c> cursor's <c>StartedAtUtc</c> before a Live tail query (C8).
        /// </summary>
        Task<ProxyExecutionEntity?> FindByItemIdAsync(string tenantId, string itemId);

        /// <summary><c>true</c> iff the tenant has at least one execution row for <paramref name="proxyId"/> (C2).</summary>
        Task<bool> AnyForProxyAsync(string tenantId, string proxyId);

        /// <summary>
        /// A single grouped aggregation over the window: row count, mean latency, <c>StatusCode &gt;= 400</c>
        /// count, and newest <c>StartedAtUtc</c>. Returns <see cref="ProxyExecutionStats.Empty"/> when no row
        /// matches.
        /// </summary>
        Task<ProxyExecutionStats> GetStatsAsync(string tenantId, string proxyId, DateTime sinceUtc);

        /// <summary>
        /// One <c>$group</c> over <paramref name="proxyIds"/>: window row count keyed by <c>ProxyId</c>.
        /// Ids with no rows are absent from the result (the caller maps them to 0). Powers
        /// <c>GetAll.calls24h</c> for a whole page in one query (H6).
        /// </summary>
        Task<IReadOnlyDictionary<string, long>> CountByProxyAsync(
            string tenantId, IReadOnlyCollection<string> proxyIds, DateTime sinceUtc);

    }

    /// <summary>Result of the Overview aggregation (SPEC &sect;3.3). All figures are over the 24 h window.</summary>
    public sealed record ProxyExecutionStats(long Count, double AvgLatencyMs, long ErrorCount, DateTime? LastCallAtUtc)
    {
        /// <summary>The zero result for a proxy with no rows in the window (C4).</summary>
        public static ProxyExecutionStats Empty { get; } = new(0, 0d, 0, null);
    }
}
