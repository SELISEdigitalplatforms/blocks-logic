using Microsoft.Extensions.Logging;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>
    /// Turns runner errors into events, at most <see cref="HealthThresholds.ErrorEventsPerMinute"/>
    /// a minute.
    /// <para>
    /// An error loop can log thousands of lines a minute; unbounded, the event file would become
    /// the next thing that fills the disk. Past the cap errors are only counted, and the count is
    /// written once as a single summary event when the minute ends.
    /// </para>
    /// </summary>
    public sealed class ErrorEventSink
    {
        private const int MaxMessageLength = 500;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        private readonly MetricsStore _store;
        private readonly TimeProvider _clock;
        private readonly object _gate = new();
        private DateTimeOffset _windowStart;
        private int _written;
        private int _dropped;

        public ErrorEventSink(MetricsStore store, TimeProvider clock)
        {
            _store = store;
            _clock = clock;
            _windowStart = clock.GetUtcNow();
        }

        /// <summary>Records one error. Only the exception's type and message are kept, never its stack.</summary>
        public void Record(LogLevel level, string category, string message, Exception? exception)
        {
            var now = _clock.GetUtcNow();
            lock (_gate)
            {
                RollWindow(now);
                if (_written >= HealthThresholds.ErrorEventsPerMinute)
                {
                    _dropped++;
                    return;
                }
                _written++;
            }
            _store.AppendEvent(new MetricsEvent(now, "error", level.ToString(), Describe(category, message, exception)));
        }

        /// <summary>Closes an elapsed minute so its dropped-count is written even if no further error arrives.</summary>
        public void Flush()
        {
            lock (_gate)
            {
                RollWindow(_clock.GetUtcNow());
            }
        }

        private void RollWindow(DateTimeOffset now)
        {
            if (now - _windowStart < Window) return;
            if (_dropped > 0)
            {
                _store.AppendEvent(new MetricsEvent(now, "error-summary", "Error",
                    $"{_dropped} more error(s) were not recorded in the last minute (limit {HealthThresholds.ErrorEventsPerMinute}/min)",
                    _dropped, HealthThresholds.ErrorEventsPerMinute));
            }
            _windowStart = now;
            _written = 0;
            _dropped = 0;
        }

        private static string Describe(string category, string message, Exception? exception)
        {
            var text = exception is null
                ? $"{category}: {message}"
                : $"{category}: {message} ({exception.GetType().Name}: {exception.Message})";
            text = text.ReplaceLineEndings(" ");
            return text.Length <= MaxMessageLength ? text : text[..MaxMessageLength];
        }
    }

    /// <summary>
    /// Hands every Error and Critical log entry to <see cref="ErrorEventSink"/>. Registered beside
    /// Genesis' Serilog provider, so it sees what the runner logs without changing where it goes.
    /// </summary>
    public sealed class ErrorEventLoggerProvider : ILoggerProvider
    {
        private readonly ErrorEventSink _sink;

        public ErrorEventLoggerProvider(ErrorEventSink sink)
        {
            _sink = sink;
        }

        public ILogger CreateLogger(string categoryName) => new ErrorLogger(categoryName, _sink);

        public void Dispose()
        {
            // Nothing to release: the sink and its store outlive every logger.
        }

        private sealed class ErrorLogger : ILogger
        {
            private readonly string _category;
            private readonly ErrorEventSink _sink;

            public ErrorLogger(string category, ErrorEventSink sink)
            {
                _category = category;
                _sink = sink;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                _sink.Record(logLevel, _category, formatter(state, exception), exception);
            }
        }
    }
}
