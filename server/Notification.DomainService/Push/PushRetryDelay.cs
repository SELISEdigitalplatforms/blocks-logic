namespace DomainService.Notification.Push
{
    /// <summary>Waits between FCM retries. A seam so tests do not sleep.</summary>
    public interface IPushRetryDelay
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushRetryDelay"/>
    public sealed class PushRetryDelay : IPushRetryDelay
    {
        private readonly TimeProvider _timeProvider;

        public PushRetryDelay(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, _timeProvider, cancellationToken);
    }
}
