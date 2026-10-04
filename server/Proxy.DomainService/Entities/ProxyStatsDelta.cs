namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// One proxy-hour's accumulated counters, ready to be applied to a <see cref="ProxyDetailEntity"/> as a
    /// single atomic update. Values are deltas to add, never absolute totals, which is what lets concurrent
    /// writers apply their batches in any order.
    /// </summary>
    public sealed class ProxyStatsDelta
    {
        public required string TenantId { get; init; }

        public required string ProxyId { get; init; }

        /// <summary>UTC hour stamp, per <c>ProxyStatsWindow.StampOf</c>.</summary>
        public required string Stamp { get; init; }

        public long Calls { get; init; }

        public long Errors { get; init; }

        public long LatencyMsTotal { get; init; }

        /// <summary>Newest call start in this batch; applied with <c>$max</c>.</summary>
        public DateTime LastCallAtUtc { get; init; }
    }
}
