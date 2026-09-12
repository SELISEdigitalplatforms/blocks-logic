using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// One hour of denormalized traffic counters. Sums only — never a mean — so two writers can each add
    /// their own deltas with <c>$inc</c> and the result is correct regardless of interleaving. The average
    /// latency the console shows is derived at read time from <see cref="LatencyMsTotal"/> / <see cref="Calls"/>.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyStatsBucket
    {
        /// <summary>Calls that produced an execution row in this hour, pre-flight rejections included.</summary>
        public long Calls { get; set; }

        /// <summary>Of those, the ones answered to the client with <c>StatusCode &gt;= 400</c>.</summary>
        public long Errors { get; set; }

        /// <summary>Sum of <c>LatencyMs</c> across <see cref="Calls"/>; <c>0</c> for pre-flight rejections.</summary>
        public long LatencyMsTotal { get; set; }
    }

    /// <summary>
    /// The rolling traffic counters kept on the proxy document itself, so the Overview tiles are one document
    /// read instead of an aggregation across <c>ProxyExecutions</c>.
    /// <para>
    /// <see cref="Buckets"/> is keyed by UTC hour stamp (<c>yyyyMMddHH</c>) rather than held as an array,
    /// because a map lets a writer <c>$inc</c> a nested field that does not exist yet: the bucket is created
    /// by the same atomic operation that increments it, so there is no read-modify-write and no upsert race
    /// between concurrent instances. Stamps older than the retained window are <c>$unset</c> on the same
    /// update, which bounds the document without ever reading it.
    /// </para>
    /// These counters are deliberately eventually-consistent: a flush lands within seconds of the calls it
    /// describes, and <c>ProxyExecutions</c> remains the source of truth for anything exact.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyStats
    {
        /// <summary>UTC hour stamp (<c>yyyyMMddHH</c>) &rarr; that hour's counters.</summary>
        public Dictionary<string, ProxyStatsBucket> Buckets { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Start time of the most recent call. Advanced with <c>$max</c>, so a late flush can never move it
        /// backwards past a newer one.
        /// </summary>
        public DateTime? LastCallAtUtc { get; set; }
    }
}
