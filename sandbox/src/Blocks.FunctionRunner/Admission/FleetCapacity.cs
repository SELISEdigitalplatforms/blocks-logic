using System.Globalization;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Admission
{
    /// <summary>
    /// The sandbox capacity of every live runner together, for the tenant share (FN-17).
    /// <para>
    /// Every runner takes any run, and a tenant's slots are counted on one Redis key for the whole
    /// fleet, so the share must be a share of the fleet: half of one host's capacity, enforced on a
    /// fleet-wide count, gave a tenant 1/(2N) of N runners — and a different limit on each host
    /// size. Read once per heartbeat (off the run path) from the heartbeats themselves.
    /// </para>
    /// <para>
    /// A runner counts only if its heartbeat was refreshed in the last <see cref="FreshMs"/>,
    /// judged by the hash's remaining TTL — Redis's own clock, so clock drift between VMs cannot
    /// drop a live runner. A clean stop deletes the heartbeat at once; a crashed runner stops
    /// counting within ~8 s. Wrong in either direction is bounded: each host still admits only
    /// what its own budget holds, and when the fleet cannot be read the host's own figure is used.
    /// </para>
    /// </summary>
    public sealed class FleetCapacity
    {
        private readonly RunnerOptions _options;
        private readonly TimeProvider _time;
        private int _fleet;
        private long _readAt;

        public FleetCapacity(IOptions<RunnerOptions> options, TimeProvider? time = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            _options = options.Value;
            _time = time ?? TimeProvider.System;
        }

        /// <summary>One heartbeat plus 3 s of slack (a slow readiness check delays the next beat).</summary>
        public int FreshMs => _options.HeartbeatMs + 3000;

        /// <summary>
        /// The capacity the tenant share is computed from: the fleet's, or <paramref name="local"/>
        /// when no fresh reading exists (none yet, Redis failing, older than three heartbeats) or
        /// it is smaller than this host — which can only mean the reading is wrong.
        /// </summary>
        public int For(int local)
        {
            var fleet = Volatile.Read(ref _fleet);
            var readAt = Volatile.Read(ref _readAt);
            if (fleet <= 0 || _time.GetElapsedTime(readAt) > TimeSpan.FromMilliseconds(3L * _options.HeartbeatMs)) return local;
            return Math.Max(fleet, local);
        }

        /// <summary>Records this runner in the fleet set; called right after its heartbeat is written.</summary>
        public Task JoinAsync(IDatabase db) => db.SetAddAsync(RedisKeys.Runners, _options.RunnerId);

        /// <summary>Takes this runner out at once on a clean stop.</summary>
        public Task LeaveAsync(IDatabase db) => db.SetRemoveAsync(RedisKeys.Runners, _options.RunnerId);

        /// <summary>
        /// Re-reads the fleet: every member's capacity and heartbeat age, in one pipelined round
        /// trip. Members whose heartbeat is gone are removed. Throws on a Redis failure; the last
        /// reading then ages out and <see cref="For"/> falls back to the host's own figure.
        /// </summary>
        public async Task RefreshAsync(IDatabase db)
        {
            ArgumentNullException.ThrowIfNull(db);
            var members = await db.SetMembersAsync(RedisKeys.Runners).ConfigureAwait(false);

            var reads = members.Select(m =>
            {
                var key = RedisKeys.Runner(m.ToString());
                return (Id: m, Capacity: db.HashGetAsync(key, "capacity"), Ttl: db.KeyTimeToLiveAsync(key));
            }).ToList();
            await Task.WhenAll(reads.SelectMany(r => new Task[] { r.Capacity, r.Ttl })).ConfigureAwait(false);

            var minTtl = RedisKeys.HeartbeatTtl - TimeSpan.FromMilliseconds(FreshMs);
            var sum = 0;
            var gone = new List<RedisValue>();
            foreach (var (id, capacity, ttl) in reads)
            {
                if (ttl.Result is not { } left)
                {
                    // No key (or one without a TTL, which no heartbeat writes): not a live runner.
                    gone.Add(id);
                    continue;
                }
                if (left < minTtl) continue;   // stale: alive at most a few seconds more
                if (int.TryParse(capacity.Result.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var c) && c > 0)
                {
                    sum += c;
                }
            }

            if (gone.Count > 0) await db.SetRemoveAsync(RedisKeys.Runners, [.. gone]).ConfigureAwait(false);

            Volatile.Write(ref _fleet, sum);
            Volatile.Write(ref _readAt, _time.GetTimestamp());
        }
    }
}
