using Blocks.FunctionRunner.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Redis
{
    /// <summary>One entry claimed from a stream.</summary>
    public sealed record ClaimedEntry(RedisValue Id, IReadOnlyDictionary<string, string> Fields)
    {
        public string? Get(string field) => Fields.TryGetValue(field, out var v) ? v : null;

        public int GetInt(string field, int fallback = 0)
            => int.TryParse(Get(field), out var v) ? v : fallback;
    }

    /// <summary>
    /// A consumer-group reader over one Redis stream.
    /// <para>
    /// Delivery is at-least-once by design (<c>plan/DECISIONS.md</c> D1). Three mechanisms make
    /// that work: a consumer group so only one runner gets an entry, <c>XAUTOCLAIM</c> so a
    /// crashed runner's work is picked up once its lease has aged out, and a delivery-count
    /// budget so a poisonous entry ends on <c>functions:dead</c> instead of circling forever.
    /// </para>
    /// <para>
    /// The entry is acknowledged only after its result is durable. Acknowledging earlier would
    /// turn a runner crash into silent data loss, which is the one failure mode the contract
    /// does not allow.
    /// </para>
    /// </summary>
    public sealed class GroupConsumer
    {
        private readonly IDatabase _db;
        private readonly ILogger _logger;
        private readonly string _stream;
        private readonly string _group;
        private readonly string _consumer;

        public GroupConsumer(IDatabase db, ILogger logger, string stream, string group, string consumer)
        {
            _db = db;
            _logger = logger;
            _stream = stream;
            _group = group;
            _consumer = consumer;
        }

        public string Stream => _stream;

        /// <summary>
        /// Creates the group if it is absent. Idempotent: BUSYGROUP means another runner won the
        /// race, which is the expected outcome on a healthy fleet.
        /// </summary>
        public async Task EnsureGroupAsync()
        {
            try
            {
                await _db.StreamCreateConsumerGroupAsync(_stream, _group, StreamPosition.Beginning, createStream: true)
                    .ConfigureAwait(false);
                _logger.LogInformation("Created consumer group {Group} on {Stream}", _group, _stream);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
            {
                // Already there.
            }
        }

        /// <summary>Reads entries never delivered to anyone, blocking briefly when idle.</summary>
        public async Task<IReadOnlyList<ClaimedEntry>> ReadNewAsync(int count, CancellationToken token)
        {
            try
            {
                var entries = await _db.StreamReadGroupAsync(
                    _stream, _group, _consumer, StreamPosition.NewMessages, count).ConfigureAwait(false);
                return Convert(entries);
            }
            catch (RedisException ex)
            {
                _logger.LogWarning("Reading {Stream} failed: {Message}", _stream, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                return [];
            }
        }

        /// <summary>
        /// Reclaims entries whose owner stopped working on them, using XAUTOCLAIM.
        /// <para>
        /// <paramref name="minIdle"/> must be comfortably longer than the lease renewal
        /// interval, or two runners will fight over the same live run. The runner uses
        /// <c>LeaseMs * 3</c>, so a runner has to miss three renewals before its work is taken.
        /// </para>
        /// </summary>
        public async Task<IReadOnlyList<ClaimedEntry>> ReclaimAbandonedAsync(TimeSpan minIdle, int count)
        {
            try
            {
                var result = await _db.StreamAutoClaimAsync(
                    _stream,
                    _group,
                    _consumer,
                    minIdleTimeInMs: (long)minIdle.TotalMilliseconds,
                    startAtId: "0-0",
                    count: count).ConfigureAwait(false);

                var claimed = result.ClaimedEntries;
                if (claimed is null || claimed.Length == 0) return [];

                _logger.LogWarning(
                    "Reclaimed {Count} abandoned entries from {Stream} after {Idle}ms idle",
                    claimed.Length, _stream, (long)minIdle.TotalMilliseconds);
                return Convert(claimed);
            }
            catch (RedisException ex)
            {
                _logger.LogWarning("Reclaiming from {Stream} failed: {Message}", _stream, ex.Message);
                return [];
            }
        }

        /// <summary>
        /// How many times this entry has been delivered, across all runners. Used to retire an
        /// entry that keeps killing whoever picks it up.
        /// </summary>
        public async Task<int> DeliveryCountAsync(RedisValue id)
        {
            try
            {
                var pending = await _db.StreamPendingMessagesAsync(
                    _stream, _group, count: 1, consumerName: RedisValue.Null,
                    minId: id, maxId: id).ConfigureAwait(false);
                return pending.Length == 0 ? 1 : (int)pending[0].DeliveryCount;
            }
            catch (RedisException)
            {
                // If the delivery count cannot be read, treat the entry as fresh rather than
                // dead-lettering work that may be perfectly good.
                return 1;
            }
        }

        /// <summary>
        /// Acknowledges an entry and deletes it. Call only once its outcome is durable.
        /// <para>
        /// The delete is what keeps the stream bounded: reading never removes an entry, and an
        /// acknowledged one can never be reclaimed again, so holding it serves nothing. It is
        /// safe because each of these streams has exactly one consumer group — <c>runners</c> on
        /// <c>functions:runs</c> and <c>functions:builds</c> — so no second reader is still owed
        /// the entry. The control plane's periodic trim is now only a net for entries nobody
        /// ever acknowledged.
        /// </para>
        /// </summary>
        public async Task AcknowledgeAsync(RedisValue id)
        {
            await _db.StreamAcknowledgeAsync(_stream, _group, id);
            await _db.StreamDeleteAsync(_stream, [id]);
        }

        /// <summary>
        /// Moves an entry that exhausted its budget to <c>functions:dead</c> and acknowledges it,
        /// so one bad job cannot block the stream for everyone else.
        /// </summary>
        public async Task DeadLetterAsync(ClaimedEntry entry, string reason)
        {
            ArgumentNullException.ThrowIfNull(entry);

            var fields = new List<NameValueEntry>(entry.Fields.Count + 3)
            {
                new("deadReason", reason),
                new("sourceStream", _stream),
                new("deadAt", DateTimeOffset.UtcNow.ToString("O")),
            };
            foreach (var (k, v) in entry.Fields) fields.Add(new NameValueEntry(k, v));

            await _db.StreamAddAsync(RedisKeys.DeadStream, [.. fields]).ConfigureAwait(false);
            await AcknowledgeAsync(entry.Id).ConfigureAwait(false);
            _logger.LogError("Dead-lettered entry {Id} from {Stream}: {Reason}", entry.Id, _stream, reason);
        }

        private static List<ClaimedEntry> Convert(StackExchange.Redis.StreamEntry[] entries)
        {
            var result = new List<ClaimedEntry>(entries.Length);
            foreach (var entry in entries)
            {
                if (entry.Values is null) continue;
                var fields = new Dictionary<string, string>(entry.Values.Length, StringComparer.Ordinal);
                foreach (var value in entry.Values)
                {
                    if (value.Name.HasValue) fields[value.Name!] = value.Value.ToString();
                }
                result.Add(new ClaimedEntry(entry.Id, fields));
            }
            return result;
        }
    }
}
