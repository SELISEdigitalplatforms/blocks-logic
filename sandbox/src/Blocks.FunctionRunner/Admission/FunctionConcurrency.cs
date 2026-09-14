using System.Globalization;
using Blocks.FunctionRunner.Contracts;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Admission
{
    /// <summary>
    /// The per-function concurrency semaphore, shared across runners through Redis.
    /// <para>
    /// A function already at its limit does not fail: its entry is left pending and picked up
    /// when a slot frees. Overflow queues, never rejects — which is why this returns null rather
    /// than throwing.
    /// </para>
    /// <para>
    /// Slots are members of a sorted set scored by their own expiry, not a counter. That
    /// distinction matters after a crash. A counter leaks: a runner killed mid-run never
    /// decrements, and the only recovery is a TTL on the whole key — which blocks the function
    /// until it fires and then frees every slot at once, including those held by sandboxes still
    /// running. With a sorted set each slot expires on its own schedule, so one crashed runner
    /// costs one slot for one slot-lifetime and nothing else. Measured before this change: two
    /// killed runners left a function pinned at 2/2, deferring every retry until the key expired.
    /// </para>
    /// <para>
    /// Expiry is scored from Redis' own clock, so runners with skewed clocks still agree.
    /// </para>
    /// </summary>
    public sealed class FunctionConcurrency : IAsyncDisposable
    {
        // Re-entry by the same run is free: a reclaimed run must not need a second slot to
        // finish what it already started.
        private const string EnterScript = """
            local now = redis.call('TIME')
            local nowMs = (tonumber(now[1]) * 1000) + math.floor(tonumber(now[2]) / 1000)
            local ttl = tonumber(ARGV[3])

            redis.call('zremrangebyscore', KEYS[1], '-inf', nowMs)

            if redis.call('zscore', KEYS[1], ARGV[2]) then
                redis.call('zadd', KEYS[1], nowMs + ttl, ARGV[2])
                redis.call('pexpire', KEYS[1], ttl * 2)
                return 1
            end

            if redis.call('zcard', KEYS[1]) >= tonumber(ARGV[1]) then
                return 0
            end

            redis.call('zadd', KEYS[1], nowMs + ttl, ARGV[2])
            redis.call('pexpire', KEYS[1], ttl * 2)
            return 1
            """;

        private const string ExitScript = """
            redis.call('zrem', KEYS[1], ARGV[1])
            if redis.call('zcard', KEYS[1]) == 0 then
                redis.call('del', KEYS[1])
            end
            return 1
            """;

        private const string HeldScript = """
            local now = redis.call('TIME')
            local nowMs = (tonumber(now[1]) * 1000) + math.floor(tonumber(now[2]) / 1000)
            redis.call('zremrangebyscore', KEYS[1], '-inf', nowMs)
            return redis.call('zcard', KEYS[1])
            """;

        /// <summary>
        /// How long a slot survives without its holder: comfortably longer than the longest legal
        /// run (60 s plus grace), short enough that a crashed runner's slot returns in a minute.
        /// </summary>
        private static readonly TimeSpan SlotTtl = TimeSpan.FromSeconds(120);

        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _member;
        private bool _released;

        private FunctionConcurrency(IDatabase db, string key, string member)
        {
            _db = db;
            _key = key;
            _member = member;
        }

        /// <summary>
        /// Takes a slot for <paramref name="runId"/>, or returns null when the function is at its
        /// limit. Taking a slot the same run already holds succeeds and refreshes it.
        /// </summary>
        public static async Task<FunctionConcurrency?> TryEnterAsync(
            IDatabase db, string functionId, string runId, int limit)
        {
            ArgumentNullException.ThrowIfNull(db);

            var effective = Math.Clamp(limit, Ceilings.MinFunctionConcurrency, Ceilings.MaxFunctionConcurrency);
            var key = RedisKeys.Concurrency(functionId);

            var entered = (long)await db.ScriptEvaluateAsync(
                EnterScript,
                [key],
                [
                    effective.ToString(CultureInfo.InvariantCulture),
                    runId,
                    ((long)SlotTtl.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                ]).ConfigureAwait(false);

            return entered == 1 ? new FunctionConcurrency(db, key, runId) : null;
        }

        /// <summary>Slots currently held for a function, ignoring any that have expired.</summary>
        public static async Task<long> HeldAsync(IDatabase db, string functionId)
        {
            ArgumentNullException.ThrowIfNull(db);
            return (long)await db.ScriptEvaluateAsync(
                HeldScript, [RedisKeys.Concurrency(functionId)]).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            try
            {
                await _db.ScriptEvaluateAsync(ExitScript, [_key], [_member]).ConfigureAwait(false);
            }
            catch (RedisException)
            {
                // The slot's own expiry score is the backstop.
            }
        }
    }
}
