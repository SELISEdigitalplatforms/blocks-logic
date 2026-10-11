using Microsoft.Extensions.Logging;

namespace XUnitTest.TestHelpers
{
    /// <summary>
    /// Keeps every log line as the sink would see it: the formatted message, every structured argument
    /// (as text) and the exception text. Used to assert a secret never reaches a log.
    /// </summary>
    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _lines = new();

        public IReadOnlyList<string> Lines
        {
            get { lock (_lines) return _lines.ToList(); }
        }

        /// <summary>Everything logged, joined, for a simple <c>NotContain</c> assertion.</summary>
        public string All => string.Join("\n", Lines);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var parts = new List<string> { formatter(state, exception) };
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                parts.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
            }
            if (exception is not null) parts.Add(exception.ToString());
            lock (_lines) _lines.Add(string.Join(" | ", parts));
        }
    }
}
