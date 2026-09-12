using System.Globalization;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>The rolled-up view of a proxy's counters over the reporting window.</summary>
    public sealed record ProxyStatsRollup(long Calls, int AvgLatencyMs, double ErrorRatePct, DateTime? LastCallAtUtc)
    {
        public static readonly ProxyStatsRollup Empty = new(0, 0, 0, null);
    }

    /// <summary>
    /// Hour-stamp arithmetic for <see cref="ProxyStats"/>, plus the read-time rollup the Overview tiles use.
    /// <para>
    /// The stamp is the Mongo field name a counter lives under, so it must be a legal key: <c>yyyyMMddHH</c>
    /// is fixed-width, ordinally sortable, and free of <c>.</c> and <c>$</c>.
    /// </para>
    /// </summary>
    public static class ProxyStatsWindow
    {
        /// <summary>Hours the tiles report over.</summary>
        public const int WindowHours = 24;

        /// <summary>
        /// Hours kept on the document. One more than <see cref="WindowHours"/>, because the oldest hour in a
        /// 24 h window is usually partial and still contributes to it.
        /// </summary>
        public const int RetainedHours = WindowHours + 1;

        /// <summary>Error-rate percentage above which the console paints the tile red.</summary>
        public const double HighErrorRatePct = 5d;

        /// <summary>The UTC hour <paramref name="instant"/> falls in, as a bucket key.</summary>
        public static string StampOf(DateTime instant) =>
            instant.ToUniversalTime().ToString("yyyyMMddHH", CultureInfo.InvariantCulture);

        /// <summary>The bucket keys inside the window ending at <paramref name="asOfUtc"/>, newest first.</summary>
        public static IEnumerable<string> StampsInWindow(DateTime asOfUtc)
        {
            var top = asOfUtc.ToUniversalTime();
            for (var i = 0; i < RetainedHours; i++)
            {
                yield return StampOf(top.AddHours(-i));
            }
        }

        /// <summary>
        /// The keys a flush should drop: the two hours that have just fallen out of the retained window.
        /// Two rather than one so a gap in traffic (no flush for an hour) still cannot strand a stale bucket.
        /// </summary>
        public static IEnumerable<string> ExpiredStamps(DateTime asOfUtc)
        {
            var top = asOfUtc.ToUniversalTime();
            yield return StampOf(top.AddHours(-RetainedHours));
            yield return StampOf(top.AddHours(-(RetainedHours + 1)));
        }

        /// <summary>
        /// Sums the in-window buckets into the tile values. Buckets outside the window are ignored rather
        /// than trusted to have been pruned, so a document that missed a prune still reports correctly.
        /// </summary>
        public static ProxyStatsRollup Rollup(ProxyStats? stats, DateTime asOfUtc)
        {
            if (stats is null || stats.Buckets.Count == 0)
            {
                return ProxyStatsRollup.Empty with { LastCallAtUtc = stats?.LastCallAtUtc };
            }

            var cutoff = StampOf(asOfUtc.ToUniversalTime().AddHours(-(RetainedHours - 1)));

            long calls = 0, errors = 0, latencyTotal = 0;
            foreach (var (stamp, bucket) in stats.Buckets)
            {
                if (string.CompareOrdinal(stamp, cutoff) < 0)
                {
                    continue;
                }

                calls += bucket.Calls;
                errors += bucket.Errors;
                latencyTotal += bucket.LatencyMsTotal;
            }

            if (calls == 0)
            {
                return ProxyStatsRollup.Empty with { LastCallAtUtc = stats.LastCallAtUtc };
            }

            return new ProxyStatsRollup(
                calls,
                (int)Math.Round((double)latencyTotal / calls, MidpointRounding.AwayFromZero),
                Math.Round(errors * 100d / calls, 1, MidpointRounding.AwayFromZero),
                stats.LastCallAtUtc);
        }
    }
}
