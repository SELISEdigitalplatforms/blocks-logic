using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Queue
{
    /// <summary>One entry claimed from a results stream, as a simple field map.</summary>
    public sealed record ResultStreamEntry(RedisValue Id, IReadOnlyDictionary<string, string> Fields)
    {
        public string? Get(string field) => Fields.TryGetValue(field, out var v) ? v : null;

        public int GetInt(string field, int fallback = 0)
            => int.TryParse(Get(field), out var v) ? v : fallback;

        public bool GetBool(string field)
            => string.Equals(Get(field), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A minimal consumer-group reader for the Worker's side of the runner contract
    /// (plan/PROTOCOL.md): <c>functions:results</c> and <c>functions:build-results</c>.
    /// <para>
    /// This is deliberately smaller than the equivalent on the Runner VM
    /// (<c>Blocks.FunctionRunner.Redis.GroupConsumer</c>), which also manages leases for
    /// execution ownership. There is no equivalent concept here: applying the same result
    /// twice is idempotent (the same status, the same result, overwriting itself), so at most
    /// one extra harmless write happens if two Worker instances briefly race a reclaimed entry
    /// — there is nothing to protect with a lease that XAUTOCLAIM's own ownership transfer
    /// does not already provide.
    /// </para>
    /// </summary>
    public sealed class FunctionResultGroupConsumer
    {
        private readonly IDatabase _db;
        private readonly ILogger _logger;
        private readonly string _stream;
        private readonly string _consumer;

        public FunctionResultGroupConsumer(IDatabase db, ILogger logger, string stream, string consumer)
        {
            _db = db;
            _logger = logger;
            _stream = stream;
            _consumer = consumer;
        }

        public async Task EnsureGroupAsync()
        {
            try
            {
                await _db.StreamCreateConsumerGroupAsync(
                    _stream, FunctionQueueKeys.LogicWorkerGroup, StreamPosition.Beginning, createStream: true);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
            {
                // Another Worker instance already created it.
            }
        }

        public async Task<IReadOnlyList<ResultStreamEntry>> ReadNewAsync(int count)
        {
            try
            {
                var entries = await _db.StreamReadGroupAsync(
                    _stream, FunctionQueueKeys.LogicWorkerGroup, _consumer, StreamPosition.NewMessages, count);
                return Convert(entries);
            }
            catch (RedisException ex)
            {
                _logger.LogWarning("Reading {Stream} failed: {Message}", _stream, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(1));
                return [];
            }
        }

        public async Task<IReadOnlyList<ResultStreamEntry>> ReclaimAbandonedAsync(TimeSpan minIdle, int count)
        {
            try
            {
                var result = await _db.StreamAutoClaimAsync(
                    _stream, FunctionQueueKeys.LogicWorkerGroup, _consumer,
                    minIdleTimeInMs: (long)minIdle.TotalMilliseconds, startAtId: "0-0", count: count);

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
        /// Acknowledges an entry and deletes it. Callers acknowledge only after the result is in
        /// Mongo, so by this point the durable record exists and the stream copy serves nothing:
        /// an acknowledged entry can never be reclaimed again. Safe because these streams have a
        /// single consumer group (<c>logic-workers</c> on <c>functions:results</c> and
        /// <c>functions:build-results</c>), so no other reader is still owed it. This is what
        /// keeps the streams bounded; <c>FunctionStreamTrimmer</c> is only the net for entries
        /// nobody ever acknowledged.
        /// </summary>
        public async Task AcknowledgeAsync(RedisValue id)
        {
            await _db.StreamAcknowledgeAsync(_stream, FunctionQueueKeys.LogicWorkerGroup, id);
            await _db.StreamDeleteAsync(_stream, [id]);
        }

        /// <summary>How many times this entry has been delivered, across all Worker instances.</summary>
        public async Task<int> DeliveryCountAsync(RedisValue id)
        {
            try
            {
                var pending = await _db.StreamPendingMessagesAsync(
                    _stream, FunctionQueueKeys.LogicWorkerGroup, count: 1, consumerName: RedisValue.Null,
                    minId: id, maxId: id);
                return pending.Length == 0 ? 1 : (int)pending[0].DeliveryCount;
            }
            catch (RedisException)
            {
                return 1;
            }
        }

        /// <summary>
        /// Moves a poison entry to <see cref="FunctionQueueKeys.DeadResultsStream"/> and
        /// acknowledges it, so one entry this Worker cannot apply — a bug, not untrusted
        /// input, since the runner is the only writer — cannot block the stream forever.
        /// </summary>
        public async Task DeadLetterAsync(ResultStreamEntry entry, string reason)
        {
            var fields = new List<NameValueEntry>(entry.Fields.Count + 3)
            {
                new("deadReason", reason),
                new("sourceStream", _stream),
                new("deadAt", DateTimeOffset.UtcNow.ToString("O")),
            };
            foreach (var (k, v) in entry.Fields) fields.Add(new NameValueEntry(k, v));

            await _db.StreamAddAsync(FunctionQueueKeys.DeadResultsStream, [.. fields]);
            await AcknowledgeAsync(entry.Id);
            _logger.LogError("Dead-lettered entry {Id} from {Stream}: {Reason}", entry.Id, _stream, reason);
        }

        private static List<ResultStreamEntry> Convert(StreamEntry[] entries)
        {
            var result = new List<ResultStreamEntry>(entries.Length);
            foreach (var entry in entries)
            {
                if (entry.Values is null) continue;
                var fields = new Dictionary<string, string>(entry.Values.Length, StringComparer.Ordinal);
                foreach (var value in entry.Values)
                {
                    if (value.Name.HasValue) fields[value.Name!] = value.Value.ToString();
                }
                result.Add(new ResultStreamEntry(entry.Id, fields));
            }
            return result;
        }
    }
}
