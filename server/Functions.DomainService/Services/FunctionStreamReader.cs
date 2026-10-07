using Blocks.Genesis;
using Functions.DomainService.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Services
{
    /// <summary>One piece of a streamed answer, or its end (<see cref="EndStatus"/> set).</summary>
    public sealed record FunctionStreamPiece(string? Data, string? EndStatus = null, string? ErrorCode = null, string? ErrorMessage = null)
    {
        public bool IsEnd => EndStatus is not null;
    }

    public interface IFunctionStreamReader
    {
        /// <summary>
        /// The run's streamed answer in order, from the first piece, ending with one end piece —
        /// or, when no end arrives in <c>Functions:StreamMaxSeconds</c>, an end of
        /// <c>TIMED_OUT</c> made up here. Stops when <paramref name="cancellationToken"/> fires.
        /// </summary>
        IAsyncEnumerable<FunctionStreamPiece> ReadAsync(string runId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Reads the Redis stream the runner fills for a streamed run (F-5): XREAD after the last id
    /// seen, so nothing is missed or repeated however far the reader falls behind; woken by the
    /// runner's nudges, polling as a floor when it cannot subscribe.
    /// </summary>
    public sealed class FunctionStreamReader(ICacheClient cache, IConfiguration configuration, ILogger<FunctionStreamReader> logger)
        : IFunctionStreamReader
    {
        private const int BatchSize = 256;
        private static readonly TimeSpan PollFloor = TimeSpan.FromMilliseconds(250);

        public async IAsyncEnumerable<FunctionStreamPiece> ReadAsync(
            string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var db = cache.CacheDatabase();
            var key = (RedisKey)FunctionQueueKeys.StreamOut(runId);
            var channel = RedisChannel.Literal(FunctionQueueKeys.StreamChannel(runId));
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, configuration.GetValue("Functions:StreamMaxSeconds", 120)));

            using var signal = new SemaphoreSlim(0, 1);
            Action<RedisChannel, RedisValue> onNudge = (_, _) =>
            {
                try { signal.Release(); }
                catch (SemaphoreFullException) { /* a read is already due */ }
                catch (ObjectDisposedException) { /* reading ended */ }
            };
            ISubscriber? subscriber = null;
            try
            {
                subscriber = db.Multiplexer?.GetSubscriber();
                if (subscriber is not null) await subscriber.SubscribeAsync(channel, onNudge);
            }
            catch (Exception ex)
            {
                subscriber = null;
                logger.LogDebug("Could not subscribe to {Channel}; reading by polling: {Message}", channel, ex.Message);
            }

            try
            {
                RedisValue after = "0-0";
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entries = await db.StreamReadAsync(key, after, BatchSize) ?? [];
                    foreach (var entry in entries)
                    {
                        after = entry.Id;
                        var end = entry[FunctionQueueKeys.StreamEndField];
                        if (!end.IsNull)
                        {
                            yield return new FunctionStreamPiece(null, end.ToString(),
                                NullIfEmpty(entry[FunctionQueueKeys.StreamCodeField]),
                                NullIfEmpty(entry[FunctionQueueKeys.StreamMessageField]));
                            yield break;
                        }
                        var data = entry[FunctionQueueKeys.StreamDataField];
                        if (!data.IsNullOrEmpty) yield return new FunctionStreamPiece(data.ToString());
                    }
                    if (entries.Length == BatchSize) continue;   // more already waiting

                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero)
                    {
                        logger.LogWarning("Stream of run {RunId} had no end within its time; closing it", runId);
                        yield return new FunctionStreamPiece(null, FunctionQueueKeys.Wire.TimedOut, "TIMED_OUT",
                            "the streamed answer did not finish in time");
                        yield break;
                    }
                    await signal.WaitAsync(left < PollFloor ? left : PollFloor, cancellationToken);
                }
            }
            finally
            {
                if (subscriber is not null)
                {
                    try { await subscriber.UnsubscribeAsync(channel, onNudge); }
                    catch (Exception ex) { logger.LogDebug("Could not unsubscribe from {Channel}: {Message}", channel, ex.Message); }
                }
            }
        }

        private static string? NullIfEmpty(RedisValue value) => value.IsNullOrEmpty ? null : value.ToString();
    }
}
