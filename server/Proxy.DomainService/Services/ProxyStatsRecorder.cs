using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Accumulates proxy traffic counters in memory and flushes them to the <c>Proxies</c> document in
    /// batches. Recording is what the request path calls; it must never touch the database.
    /// </summary>
    public interface IProxyStatsRecorder
    {
        /// <summary>
        /// Adds one call to the in-memory buffer. Non-blocking, allocation-light, and never throws: a
        /// counter is not worth failing a relayed response for.
        /// </summary>
        void Record(string tenantId, string proxyId, int statusCode, int latencyMs, DateTime startedAtUtc);

        /// <summary>Writes everything buffered so far. Called on a timer, and once on shutdown.</summary>
        Task FlushAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The default recorder: a lock-free buffer keyed by <c>(tenant, proxy, hour)</c>, drained on a timer by
    /// <see cref="ProxyStatsFlushService"/>.
    /// <para>
    /// Two properties matter. First, the request path does no database work at all — a relayed call costs one
    /// dictionary update, so thousands of calls to one proxy in a flush interval collapse into a single
    /// write. Second, that write is a pure <c>$inc</c> / <c>$max</c>, which the server applies atomically:
    /// concurrent instances flushing the same proxy-hour add to each other rather than overwrite, with no
    /// read-modify-write and no transaction.
    /// </para>
    /// The trade is staleness bounded by the flush interval, and the loss of at most one interval's counters
    /// if the process dies. Both are acceptable for tiles whose exact figures can always be recomputed from
    /// <c>ProxyExecutions</c>.
    /// </summary>
    public sealed class ProxyStatsRecorder : IProxyStatsRecorder
    {
        private readonly IProxyRepository _proxyRepository;
        private readonly ILogger<ProxyStatsRecorder> _logger;

        /// <summary>Guards against an unbounded buffer if the database is unreachable for a long time.</summary>
        private const int MaxBufferedKeys = 10_000;

        private readonly ConcurrentDictionary<(string TenantId, string ProxyId, string Stamp), Counter> _buffer =
            new();

        public ProxyStatsRecorder(IProxyRepository proxyRepository, ILogger<ProxyStatsRecorder> logger)
        {
            _proxyRepository = proxyRepository;
            _logger = logger;
        }

        public void Record(string tenantId, string proxyId, int statusCode, int latencyMs, DateTime startedAtUtc)
        {
            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(proxyId))
            {
                return;
            }

            var key = (tenantId, proxyId, ProxyStatsWindow.StampOf(startedAtUtc));

            // A miss on an already-full buffer is dropped rather than growing it; an existing key is still
            // incremented, so a hot proxy keeps counting while the database is unavailable.
            if (!_buffer.TryGetValue(key, out var counter))
            {
                if (_buffer.Count >= MaxBufferedKeys)
                {
                    return;
                }

                counter = _buffer.GetOrAdd(key, _ => new Counter());
            }

            counter.Add(statusCode >= 400, latencyMs, startedAtUtc);
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            if (_buffer.IsEmpty)
            {
                return;
            }

            // Take each key out before reading it, so calls arriving mid-flush accumulate into a fresh
            // counter and are picked up by the next pass instead of being lost with this one.
            var deltas = new List<ProxyStatsDelta>();
            foreach (var key in _buffer.Keys)
            {
                if (!_buffer.TryRemove(key, out var counter))
                {
                    continue;
                }

                var (calls, errors, latencyTotal, lastCallAt) = counter.Snapshot();
                if (calls == 0)
                {
                    continue;
                }

                deltas.Add(new ProxyStatsDelta
                {
                    TenantId = key.TenantId,
                    ProxyId = key.ProxyId,
                    Stamp = key.Stamp,
                    Calls = calls,
                    Errors = errors,
                    LatencyMsTotal = latencyTotal,
                    LastCallAtUtc = lastCallAt,
                });
            }

            if (deltas.Count == 0)
            {
                return;
            }

            foreach (var group in deltas.GroupBy(d => d.TenantId, StringComparer.Ordinal))
            {
                try
                {
                    await _proxyRepository.ApplyStatsDeltasAsync(group.Key, group.ToList(), cancellationToken);
                }
                catch (Exception ex)
                {
                    // Dropped, not re-buffered: retrying would risk double-counting a batch that partially
                    // applied, and these counters are a convenience over ProxyExecutions, not a ledger.
                    _logger.LogWarning(ex,
                        "Proxy stats: dropped {Count} counter batch(es) for tenant {TenantId} after a write failure.",
                        group.Count(), group.Key);
                }
            }
        }

        /// <summary>
        /// A mutable cell accumulated under its own lock. The lock is taken only for the few instructions
        /// that update four fields together, which is cheaper and far simpler to reason about than four
        /// independent interlocked operations that could be snapshotted mid-update.
        /// </summary>
        private sealed class Counter
        {
            private readonly object _gate = new();
            private long _calls;
            private long _errors;
            private long _latencyMsTotal;
            private DateTime _lastCallAtUtc;

            public void Add(bool isError, int latencyMs, DateTime startedAtUtc)
            {
                lock (_gate)
                {
                    _calls++;
                    if (isError)
                    {
                        _errors++;
                    }

                    _latencyMsTotal += Math.Max(0, latencyMs);
                    if (startedAtUtc > _lastCallAtUtc)
                    {
                        _lastCallAtUtc = startedAtUtc;
                    }
                }
            }

            public (long Calls, long Errors, long LatencyMsTotal, DateTime LastCallAtUtc) Snapshot()
            {
                lock (_gate)
                {
                    return (_calls, _errors, _latencyMsTotal, _lastCallAtUtc);
                }
            }
        }
    }
}
