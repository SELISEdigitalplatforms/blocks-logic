using System.Collections.Concurrent;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Per-upstream-host circuit breaker, in this process.
    /// <para>
    /// Keyed by a <b>scope</b> and the host. The gateway's scope is tenant + proxy + caller type (PS-8,
    /// 2026-10-07; see <c>ProxyGatewayService.BreakerScope</c>): an anonymous flood of slow calls on one public
    /// proxy then opens only that proxy's "anonymous" circuit, and signed-in callers, workflow steps and the
    /// tenant's other proxies to the same vendor keep working. The cost: a real outage is found per scope, a
    /// few failed calls each. The tenant is always part of the scope, so one tenant never opens another's.
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

            /// <summary>
            /// When the probe slot frees itself even if the probe never reports (PX-3). A probe whose caller
            /// hung up, whose response was too large, or that hit an unexpected error records neither success
            /// nor failure; without this the circuit would refuse every call until the process restarts.
            /// </summary>
            public DateTimeOffset ProbeExpiresAt;
        }

        private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);
        private readonly TimeProvider _time;

        public ProxyCircuitBreaker(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

        private static string Key(string scope, string host) => scope + "|" + host;

        /// <inheritdoc />
        public bool IsOpen(string scope, string host, ProxyBreakerConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (!_states.TryGetValue(Key(scope, host), out var state)) return false;

            lock (state)
            {
                if (state.OpenedUntil == default || _time.GetUtcNow() >= state.OpenedUntil)
                {
                    // Closed, or the open window has elapsed. Half-open: let exactly one caller through to
                    // find out whether the host is back, and keep refusing the rest until it reports.
                    if (state.OpenedUntil == default) return false;

                    var now = _time.GetUtcNow();
                    if (state.ProbeInFlight && now < state.ProbeExpiresAt) return true;

                    // No probe, or the last one went silent: this caller is the probe. It holds the slot for
                    // one open window at most, so a silent probe costs no more than one more window.
                    state.ProbeInFlight = true;
                    state.ProbeExpiresAt = now.AddSeconds(config.OpenSeconds);
                    return false;
                }

                return true;
            }
        }

        /// <inheritdoc />
        public void RecordSuccess(string scope, string host)
        {
            if (!_states.TryGetValue(Key(scope, host), out var state)) return;

            lock (state)
            {
                state.ConsecutiveFailures = 0;
                state.OpenedUntil = default;
                state.ProbeInFlight = false;
            }
        }

        /// <inheritdoc />
        public void RecordFailure(string scope, string host, ProxyBreakerConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var state = _states.GetOrAdd(Key(scope, host), _ => new State());

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
        /// <see cref="RecordFailure"/>. A probe that never reports frees its slot after one open window.
        /// </summary>
        /// <param name="scope">Who shares this circuit; always starts with the tenant id.</param>
        bool IsOpen(string scope, string host, ProxyBreakerConfig config);

        /// <summary>Closes the circuit and clears the failure count.</summary>
        void RecordSuccess(string scope, string host);

        /// <summary>Counts a failure, opening the circuit once the threshold is reached.</summary>
        void RecordFailure(string scope, string host, ProxyBreakerConfig config);
    }
}
