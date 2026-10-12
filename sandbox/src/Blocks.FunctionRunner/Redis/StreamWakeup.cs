using StackExchange.Redis;

namespace Blocks.FunctionRunner.Redis
{
    /// <summary>
    /// Lets an idle stream reader wake on a pub/sub nudge instead of sleeping its whole poll
    /// interval. <see cref="WaitAsync"/> returns at the first nudge or when the interval runs out,
    /// whichever comes first, so the poll is still the floor: no nudge, a dropped subscription,
    /// or a writer that predates nudges all mean exactly the old behaviour.
    /// <para>
    /// A nudge that arrives while the reader is busy is remembered (one, not a count), so the next
    /// wait returns at once and the reader looks again — never a missed entry, at most one empty read.
    /// </para>
    /// </summary>
    public sealed class StreamWakeup : IDisposable
    {
        private readonly SemaphoreSlim _signal = new(0, 1);
        private readonly ISubscriber? _subscriber;
        private readonly RedisChannel _channel;

        private StreamWakeup(ISubscriber? subscriber, RedisChannel channel)
        {
            _subscriber = subscriber;
            _channel = channel;
        }

        /// <summary>Subscribes to <paramref name="channel"/>; on any failure, a wake-up that only polls.</summary>
        public static async Task<StreamWakeup> SubscribeAsync(IDatabase db, string channel)
        {
            var name = RedisChannel.Literal(channel);
            try
            {
                var subscriber = db.Multiplexer.GetSubscriber();
                var wakeup = new StreamWakeup(subscriber, name);
                await subscriber.SubscribeAsync(name, (_, _) => wakeup.Nudge()).ConfigureAwait(false);
                return wakeup;
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException or NotSupportedException)
            {
                return new StreamWakeup(null, name);
            }
        }

        /// <summary>Waits up to <paramref name="interval"/>, or less if nudged.</summary>
        public Task WaitAsync(TimeSpan interval, CancellationToken token) => _signal.WaitAsync(interval, token);

        internal void Nudge()
        {
            try { _signal.Release(); }
            catch (SemaphoreFullException) { /* already signalled */ }
            catch (ObjectDisposedException) { /* shutting down */ }
        }

        /// <summary>
        /// Tells idle readers of a stream that it has a new entry. Fire-and-forget: it is only a
        /// hint, and the entry itself is already durable, so it never delays or fails the writer.
        /// </summary>
        public static void Publish(IDatabase db, string channel) => Publish(db, channel, "1");

        /// <summary>The same fire-and-forget hint, carrying a message (e.g. a run's final status).</summary>
        public static void Publish(IDatabase db, string channel, string message)
        {
            try
            {
                db.Publish(RedisChannel.Literal(channel), message, CommandFlags.FireAndForget);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                // A hint, nothing more: readers still poll.
            }
        }

        public void Dispose()
        {
            try { _subscriber?.Unsubscribe(_channel, flags: CommandFlags.FireAndForget); }
            catch (Exception ex) when (ex is RedisException or ObjectDisposedException) { }
            _signal.Dispose();
        }
    }
}
