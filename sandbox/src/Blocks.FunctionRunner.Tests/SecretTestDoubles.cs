using System.Collections.Concurrent;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.SecretStore;
using Microsoft.Extensions.Logging;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>A resolver that answers from a dictionary and records what it was asked.</summary>
    internal sealed class FakeRunSecretResolver : IRunSecretResolver
    {
        private readonly Dictionary<string, string> _values;
        private readonly Exception? _throw;

        public FakeRunSecretResolver(Dictionary<string, string>? values = null, Exception? @throw = null)
        {
            _values = values ?? [];
            _throw = @throw;
        }

        public List<(string TenantId, EnvSecretReferences.Caller Caller, string[] Ids)> Calls { get; } = [];

        public Dictionary<string, string> Reasons { get; } = [];

        public Task<SecretLookup> ResolveAsync(
            string tenantId, EnvSecretReferences.Caller caller, IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken)
        {
            Calls.Add((tenantId, caller, secretIds.ToArray()));
            if (_throw is not null) throw _throw;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var unresolved = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in secretIds)
            {
                if (_values.TryGetValue(id, out var v)) values[id] = v;
                else unresolved[id] = Reasons.TryGetValue(id, out var r) ? r : SecretUnresolvedReasons.NotFound;
            }
            return Task.FromResult(new SecretLookup(values, unresolved));
        }
    }

    /// <summary>
    /// Collects every log line — the rendered message, every structured argument and any
    /// exception with its inner chain — so a test can assert a value never reached a log.
    /// </summary>
    internal sealed class CapturingLoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public string All => string.Join("\n", Lines);

        public ILogger<T> For<T>() => new Typed<T>(CreateLogger(typeof(T).FullName!));

        public ILogger CreateLogger(string categoryName) => new Capturing(this, categoryName);

        private sealed class Capturing(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var parts = new List<string> { $"{logLevel} {category}: {formatter(state, exception)}" };
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    parts.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
                }
                for (var e = exception; e is not null; e = e.InnerException)
                {
                    parts.Add(e.ToString());
                }
                owner.Lines.Enqueue(string.Join(" | ", parts));
            }
        }

        private sealed class Typed<T>(ILogger inner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
