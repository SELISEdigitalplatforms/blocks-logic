using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>
    /// Writes a sample every 10 seconds and an event when a sample crosses a threshold.
    /// <para>
    /// This is the runner's own black-box recorder: when something goes wrong on the host, the
    /// last hour of numbers is on disk and <c>fnctl stats</c> reads them, whether or not the
    /// runner is still serving. It does not act on anything — it only records. Failing to record
    /// is logged (at most once a minute) and never stops the runner.
    /// </para>
    /// </summary>
    public sealed class MetricsRecorder : BackgroundService
    {
        private static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(1);

        private readonly SampleCollector _collector;
        private readonly MetricsStore _store;
        private readonly ErrorEventSink _errors;
        private readonly TimeProvider _clock;
        private readonly ILogger<MetricsRecorder> _logger;
        private readonly EventGate _gate = new();
        private DateTimeOffset _lastWarning = DateTimeOffset.MinValue;

        public MetricsRecorder(
            SampleCollector collector,
            MetricsStore store,
            ErrorEventSink errors,
            TimeProvider clock,
            ILogger<MetricsRecorder> logger)
        {
            _collector = collector;
            _store = store;
            _errors = errors;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _store.WriteFailed += WarnOncePerMinute;

            while (!stoppingToken.IsCancellationRequested)
            {
                await RecordOnceAsync(stoppingToken).ConfigureAwait(false);

                try
                {
                    await Task.Delay(HealthThresholds.SampleInterval, _clock, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RecordOnceAsync(CancellationToken token)
        {
            try
            {
                var sample = await _collector.CollectAsync(token).ConfigureAwait(false);
                _store.AppendSample(sample);
                foreach (var breach in _gate.Admit(HealthRules.Evaluate(sample), sample.Ts))
                {
                    _store.AppendEvent(new MetricsEvent(sample.Ts, breach.Key, "Warning", breach.Message, breach.Value, breach.Threshold));
                }

                _errors.Flush();
                _store.PruneSamples();
                _store.PruneDaily(MetricsStore.EventsPrefix);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                WarnOncePerMinute($"sampling failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void WarnOncePerMinute(string reason)
        {
            var now = _clock.GetUtcNow();
            if (now - _lastWarning < WarnEvery) return;
            _lastWarning = now;
            _logger.LogWarning("Metrics recording problem: {Reason}", reason);
        }
    }
}
