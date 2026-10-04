using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Drains <see cref="IProxyStatsRecorder"/> on a fixed interval, and once more on shutdown so a graceful
    /// stop does not discard the last interval's counters.
    /// <para>
    /// The interval is the staleness budget for the Overview tiles and the only knob that trades freshness
    /// against write volume: one write per active proxy per interval, regardless of traffic.
    /// </para>
    /// </summary>
    public sealed class ProxyStatsFlushService : BackgroundService
    {
        /// <summary>How often buffered counters are written. Also the tiles' worst-case staleness.</summary>
        public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(15);

        private readonly IProxyStatsRecorder _recorder;
        private readonly ILogger<ProxyStatsFlushService> _logger;

        public ProxyStatsFlushService(IProxyStatsRecorder recorder, ILogger<ProxyStatsFlushService> logger)
        {
            _recorder = recorder;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(FlushInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await FlushSafelyAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown; fall through to the final drain below.
            }

            // Not passing stoppingToken: it is already cancelled here, and this last write is the whole
            // reason the final drain exists.
            await FlushSafelyAsync(CancellationToken.None);
        }

        private async Task FlushSafelyAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _recorder.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The recorder already handles per-tenant write failures; anything reaching here is
                // unexpected, and must not take the host's background service down with it.
                _logger.LogError(ex, "Proxy stats: flush pass failed.");
            }
        }
    }
}
