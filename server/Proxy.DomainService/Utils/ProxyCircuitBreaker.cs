using System.Collections.Concurrent;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Per-upstream-host circuit breaker, in this process.
    /// <para>
    /// Keyed by tenant <b>and</b> host: the failure belongs to the host, so every route pointing at it trips
    /// together rather than each discovering the outage on its own. Keeping it per tenant stops one tenant's
    /// broken vendor from opening a circuit for another tenant calling the same public API.
    /// </para>
    /// <para>
    /// Deliberately in-process rather than in Redis. A breaker exists to stop <i>this</i> process tying up
    /// its own connections and threads against a dead host, which it can decide alone; putting the state in
    /// Redis would add a round trip to every forward to make instances agree about something each can
    /// observe for itself. If the fleet ever needs one shared verdict, that is a different design and this
    /// type is where it would change.
    /// </para>
    /// </summary>
    public sealed class ProxyCircuitBreaker : IProxyCircuitBreaker
    {
        private sealed class State
        {
            public int ConsecutiveFailures;
            public DateTimeOffset OpenedUntil;

            /// <summary>Set while one probe is in flight, so half-open admits exactly one caller.</summary>
            public bool ProbeInFlight;
        }

        private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);
        private readonly TimeProvider _time;

        public ProxyCircuitBreaker(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

        private static string Key(string tenantId, string host) => tenantId + "|" + host;

        /// <inheritdoc />
        public bool IsOpen(string tenantId, string host, ProxyBreakerConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (!_states.TryGetValue(Key(tenantId, host), out var state)) return false;

            lock (state)
            {
                if (state.OpenedUntil == default || _time.GetUtcNow() >= state.OpenedUntil)
                {
                    // Closed, or the open window has elapsed. Half-open: let exactly one caller through to
                    // find out whether the host is back, and keep refusing the rest until it reports.
                    if (state.OpenedUntil == default) return false;
                    if (state.ProbeInFlight) return true;

                    state.ProbeInFlight = true;
                    return false;
                }

                return true;
            }
        }

        /// <inheritdoc />
        public void RecordSuccess(string tenantId, string host)
        {
            if (!_states.TryGetValue(Key(tenantId, host), out var state)) return;

            lock (state)
            {
                state.ConsecutiveFailures = 0;
                state.OpenedUntil = default;
                state.ProbeInFlight = false;
            }
        }

        /// <inheritdoc />
        public void RecordFailure(string tenantId, string host, ProxyBreakerConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var state = _states.GetOrAdd(Key(tenantId, host), _ => new State());

            lock (state)
            {
                state.ProbeInFlight = false;
                state.ConsecutiveFailures++;

                if (state.ConsecutiveFailures >= config.FailureThreshold)
                {
                    state.OpenedUntil = _time.GetUtcNow().AddSeconds(config.OpenSeconds);
                }
            }
        }
    }

    /// <summary>The breaker, as the gateway sees it.</summary>
    public interface IProxyCircuitBreaker
    {
        /// <summary>
        /// True when this host is being refused right now. A half-open probe returns false for exactly one
        /// caller, which is then expected to report back through <see cref="RecordSuccess"/> or
        /// <see cref="RecordFailure"/>.
        /// </summary>
        bool IsOpen(string tenantId, string host, ProxyBreakerConfig config);

        /// <summary>Closes the circuit and clears the failure count.</summary>
        void RecordSuccess(string tenantId, string host);

        /// <summary>Counts a failure, opening the circuit once the threshold is reached.</summary>
        void RecordFailure(string tenantId, string host, ProxyBreakerConfig config);
    }
}
