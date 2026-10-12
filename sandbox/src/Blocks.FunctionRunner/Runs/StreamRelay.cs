using Blocks.FunctionRunner.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Hands one run's streamed answer to the Api as it is produced (F-5): each piece is appended
    /// to the run's Redis stream (<see cref="RedisKeys.StreamOut"/>) and the reader is nudged, then
    /// one closing entry carries the final status.
    /// <para>
    /// Called from the sandbox read loop, so nothing here waits: the commands go out back to back
    /// on the one connection, which keeps them in order, and only <see cref="EndAsync"/> waits —
    /// for the last of them. A Redis stream rather than pub/sub alone, because a reader that is
    /// slow, or subscribes a moment late, must still get every piece in order.
    /// </para>
    /// </summary>
    internal sealed class StreamRelay(IDatabase db, string runId, ILogger logger)
    {
        private readonly RedisKey _key = RedisKeys.StreamOut(runId);
        private readonly RedisChannel _channel = RedisChannel.Literal(RedisKeys.StreamChannel(runId));
        private Task _last = Task.CompletedTask;
        private bool _ended;
        private int _failures;

        /// <summary>At least one piece was sent: the caller is reading a stream, so it must get an end.</summary>
        public bool Started { get; private set; }

        public void Send(string data)
        {
            if (_ended) return;
            var first = !Started;
            Started = true;
            _last = Watch(db.StreamAddAsync(_key, RedisKeys.StreamDataField, data));
            if (first) Watch(db.KeyExpireAsync(_key, RedisKeys.StreamOutTtl));
            Watch(db.PublishAsync(_channel, "c"));
        }

        /// <summary>
        /// Closes the stream with the run's status (once; only if it started). Waits for every
        /// piece before it, so a caller told "done" has been given everything.
        /// </summary>
        public async Task EndAsync(string status, string? errorCode, string? errorMessage)
        {
            if (!Started || _ended) return;
            _ended = true;
            try
            {
                var end = db.StreamAddAsync(_key,
                [
                    new NameValueEntry(RedisKeys.StreamEndField, status),
                    new NameValueEntry(RedisKeys.StreamCodeField, errorCode ?? string.Empty),
                    new NameValueEntry(RedisKeys.StreamMessageField, errorMessage ?? string.Empty),
                ]);
                var nudge = db.PublishAsync(_channel, "e");
                await Task.WhenAll(_last, end, nudge).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not close the stream of run {RunId} ({ExceptionType})", runId, ex.GetType().Name);
            }
        }

        private Task Watch(Task task)
        {
            _ = task.ContinueWith(t =>
            {
                // One line per run, not per piece: a Redis outage would otherwise log thousands.
                if (Interlocked.Increment(ref _failures) == 1)
                {
                    logger.LogWarning("Could not relay part of the stream of run {RunId}: {Message}",
                        runId, t.Exception?.GetBaseException().Message);
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return task;
        }
    }
}
