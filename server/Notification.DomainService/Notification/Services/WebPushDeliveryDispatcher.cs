using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification
{
    /// <summary>
    /// Runs each delivery on its own background task. The queue consumer returns as soon as the
    /// task is started, so one device waiting 5s or 30s between attempts does not block delivery
    /// to anyone else. Retries in flight are lost if the Worker restarts; there is no dead-letter
    /// queue in this pass.
    /// </summary>
    public sealed class WebPushDeliveryDispatcher : IWebPushDeliveryDispatcher
    {
        private readonly IWebPushDeliveryService _deliveryService;
        private readonly ILogger<WebPushDeliveryDispatcher> _logger;
        private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

        public WebPushDeliveryDispatcher(IWebPushDeliveryService deliveryService, ILogger<WebPushDeliveryDispatcher> logger)
        {
            _deliveryService = deliveryService;
            _logger = logger;
        }

        /// <summary>Deliveries started and not yet finished.</summary>
        public int InFlightCount => _inFlight.Count;

        public void Dispatch(WebPushDeliveryCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            var task = Task.Run(() => RunAsync(command));
            _inFlight.TryAdd(task, 0);
            _ = task.ContinueWith(t => _inFlight.TryRemove(t, out _), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /// <summary>Completes when every delivery started so far has finished.</summary>
        public Task DrainAsync() => Task.WhenAll(_inFlight.Keys);

        private async Task RunAsync(WebPushDeliveryCommand command)
        {
            try
            {
                await _deliveryService.DeliverAsync(command).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebPush: delivery task failed for user {UserId}", command.UserId);
            }
        }
    }
}
