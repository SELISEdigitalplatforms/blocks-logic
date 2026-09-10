using Blocks.Genesis;
using Functions.DomainService.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Consumers
{
    /// <summary>
    /// Trims the Functions Redis streams by age, because nothing else does.
    /// <para>
    /// A stream entry is <b>not</b> removed when it is read. <c>XREADGROUP</c> moves it into the
    /// consumer group's pending list and <c>XACK</c> removes it from that list — the entry itself
    /// stays in the stream forever unless trimmed. Mongo is the durable record (the Worker writes
    /// the run and its logs before acknowledging), and the per-run payload keys expire on their
    /// own after <see cref="FunctionQueueKeys.RunTtl"/>, so without this sweep the streams were
    /// the one unbounded thing in the design — on a shared Redis that Genesis also uses.
    /// </para>
    /// <para>
    /// <b>Trimming by age, not by length.</b> Measured against Redis 8.10.1 rather than assumed:
    /// a trim <i>does</i> remove an entry that is still pending — <c>XTRIM MINID</c> evicted a
    /// pending entry, <c>XPENDING</c> still counted it, <c>XACK</c> of it returned 1, and
    /// <c>XAUTOCLAIM</c> then dropped the tombstone from the pending list. So the group repairs
    /// itself, but the entry is gone. That is exactly why the boundary is an <b>age</b> above
    /// <see cref="FunctionQueueKeys.RunTtl"/> and not a length: an entry older than the payload
    /// TTL has no envelope, result or logs left, so nothing could have re-executed it anyway,
    /// whereas <c>MAXLEN</c> would evict by position and could drop live work during a backlog.
    /// </para>
    /// <para>
    /// Since consumers now delete each entry as they acknowledge it
    /// (<c>FunctionResultGroupConsumer.AcknowledgeAsync</c>, and the runner's
    /// <c>GroupConsumer</c>), this sweep is a <b>net</b> rather than the mechanism: what reaches
    /// it are entries nobody ever acknowledged — written while no consumer was running, or
    /// abandoned by a group that no longer exists. Retention is therefore short.
    /// </para>
    /// <para>
    /// Idempotent and safe to run from several Worker instances at once: trimming to the same
    /// minimum id twice removes nothing the first pass did not.
    /// </para>
    /// </summary>
    public sealed class FunctionStreamTrimmer : BackgroundService
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

        /// <summary>
        /// Twice <see cref="FunctionQueueKeys.RunTtl"/>: an entry older than the payload TTL has
        /// no envelope, result or logs left and could never be re-executed, and doubling it keeps
        /// the invariant obvious — working retention must always exceed the payload TTL.
        /// </summary>
        private static readonly TimeSpan DefaultWorkingRetention = TimeSpan.FromHours(12);

        /// <summary>
        /// Dead letters are forensic evidence of work that was lost, so they outlive the working
        /// streams — long enough to survive a weekend and be noticed on Monday, not long enough
        /// to accumulate silently. Mongo keeps the run record itself for 30 days regardless.
        /// </summary>
        private static readonly TimeSpan DefaultDeadRetention = TimeSpan.FromDays(7);

        private static readonly string[] WorkingStreams =
        [
            FunctionQueueKeys.RunsStream,
            FunctionQueueKeys.ResultsStream,
            FunctionQueueKeys.BuildsStream,
            FunctionQueueKeys.BuildResultsStream,
        ];

        private static readonly string[] DeadStreams =
        [
            FunctionQueueKeys.DeadStream,
            FunctionQueueKeys.DeadResultsStream,
        ];

        private readonly IDatabase _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FunctionStreamTrimmer> _logger;

        public FunctionStreamTrimmer(
            ICacheClient cache, IConfiguration configuration, ILogger<FunctionStreamTrimmer> logger)
        {
            _db = cache.CacheDatabase();
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Sweep once at startup so a Worker that has been down over a long weekend does not
            // wait another hour before reclaiming the space.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // Retention is housekeeping: never take the Worker down over it.
                    _logger.LogError(ex, "Trimming the Functions streams failed; will retry on the next sweep");
                }

                try
                {
                    await Task.Delay(SweepInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        internal async Task<long> SweepOnceAsync(CancellationToken cancellationToken)
        {
            // Floored at the payload TTL, not merely defaulted. A configured value below it would
            // trim entries whose envelope is still alive — that is, live work — which is the one
            // way this sweep can lose a run. Configuration should not be able to express it.
            var workingRetention = ResolveRetention(
                "Functions:StreamRetentionHours", DefaultWorkingRetention, floor: FunctionQueueKeys.RunTtl);
            var deadRetention = ResolveRetention(
                "Functions:DeadStreamRetentionHours", DefaultDeadRetention, floor: FunctionQueueKeys.RunTtl);

            var removed = 0L;
            removed += await TrimAsync(WorkingStreams, workingRetention, cancellationToken);
            removed += await TrimAsync(DeadStreams, deadRetention, cancellationToken);
            return removed;
        }

        private async Task<long> TrimAsync(string[] streams, TimeSpan retention, CancellationToken cancellationToken)
        {
            // A stream id is "<unix-milliseconds>-<sequence>", so a bare millisecond value is a
            // valid exclusive-of-older minimum id.
            var cutoff = DateTimeOffset.UtcNow.Subtract(retention).ToUnixTimeMilliseconds();
            if (cutoff <= 0) return 0;

            var removed = 0L;
            foreach (var stream in streams)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await _db.KeyExistsAsync(stream)) continue;

                // Approximate trimming: Redis stops at a node boundary rather than walking the
                // whole stream, which is what makes this cheap on a large one. The cost is that
                // a few entries slightly older than the cutoff can survive to the next sweep,
                // which does not matter for retention.
                var trimmed = await _db.StreamTrimByMinIdAsync(
                    stream, cutoff.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    useApproximateMaxLength: true);

                if (trimmed > 0)
                {
                    removed += trimmed;
                    _logger.LogInformation(
                        "Trimmed {Count} entries older than {RetentionHours}h from {Stream}",
                        trimmed, retention.TotalHours, stream);
                }
            }

            return removed;
        }

        /// <summary>
        /// Hours from configuration when set to a positive value, otherwise the default — and
        /// never below <paramref name="floor"/>, which is the payload TTL. A misconfiguration is
        /// reported rather than silently obeyed, because obeying it would delete live work.
        /// </summary>
        private TimeSpan ResolveRetention(string key, TimeSpan fallback, TimeSpan floor)
        {
            var hours = _configuration.GetValue<double?>(key);
            if (hours is not > 0) return fallback;

            var configured = TimeSpan.FromHours(hours.Value);
            if (configured > floor) return configured;

            _logger.LogWarning(
                "{Key}={Hours}h is not above the {FloorHours}h payload TTL and would trim entries that "
                + "can still be acted on; using {UsedHours}h instead",
                key, hours.Value, floor.TotalHours, fallback.TotalHours);
            return fallback;
        }
    }
}
