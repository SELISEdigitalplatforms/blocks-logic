using System.Collections.Concurrent;

namespace Blocks.FunctionRunner.SecretStore
{
    /// <summary>Whether the secret store is worth asking right now, per tenant.</summary>
    public interface ISecretStoreBreaker
    {
        /// <summary>
        /// True when this tenant's store is being left alone after a recent failure. One caller
        /// per window is let through to find out whether it is back; the rest are told to wait.
        /// </summary>
        bool ShouldSkip(string? tenantId);

        /// <summary>The store answered. Closes the circuit.</summary>
        void RecordSuccess(string? tenantId);

        /// <summary>
        /// The store could not be reached. Opens the circuit for <see cref="OpenWindow"/>.
        /// <para>
        /// Only ever called for <c>SECRET_STORE_UNAVAILABLE</c>. A secret that does not exist, or
        /// that this caller may not read, says nothing about the store's health and must not stop
        /// every other function from running.
        /// </para>
        /// </summary>
        void RecordUnavailable(string? tenantId);
    }

    /// <summary>
    /// Stops an unreachable secret store from draining a host's capacity.
    /// <para>
    /// Resolution happens after a run has taken a host slot, a tenant slot and one of the
    /// function's own — deliberately, because the plaintext should exist for as little time as
    /// possible and as close to the sandbox as possible. The cost of that ordering shows up when
    /// the store is down: every run holds three slots for the full timeout, achieves nothing, and
    /// is retried, so a host's usable capacity drains away while every function looks idle.
    /// </para>
    /// <para>
    /// Asked <em>before</em> any slot is taken, this turns that into runs queueing: nothing is
    /// held, nothing is spent, and everything drains when the store comes back. It does not move
    /// the resolution, so the ordering that keeps plaintext short-lived is untouched.
    /// </para>
    /// <para>
    /// Per tenant, because one tenant's vault being unreachable says nothing about another's. In
    /// process, like <c>ProxyCircuitBreaker</c>: the question "is it worth me asking" is one this
    /// host can answer alone, and a shared answer would cost a round trip on every run to agree
    /// about something each host can see for itself.
    /// </para>
    /// </summary>
    public sealed class SecretStoreBreaker : ISecretStoreBreaker
    {
        /// <summary>
        /// How long the store is left alone after a failure. Short: an outage that has ended should
        /// be noticed quickly, and the cost of finding out is one deferred run, not a failed one.
        /// </summary>
        public static readonly TimeSpan OpenWindow = TimeSpan.FromSeconds(15);

        private sealed class State
        {
            public DateTimeOffset OpenedUntil;
            public bool ProbeInFlight;
        }

        private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);
        private readonly TimeProvider _time;

        public SecretStoreBreaker(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

        private static string Key(string? tenantId) => tenantId ?? "(none)";

        /// <inheritdoc />
        public bool ShouldSkip(string? tenantId)
        {
            if (!_states.TryGetValue(Key(tenantId), out var state)) return false;

            lock (state)
            {
                if (state.OpenedUntil == default) return false;

                if (_time.GetUtcNow() >= state.OpenedUntil)
                {
                    // The window has passed. Let exactly one run through to find out whether the
                    // store is back, and keep deferring the rest until it reports.
                    if (state.ProbeInFlight) return true;

                    state.ProbeInFlight = true;
                    return false;
                }

                return true;
            }
        }

        /// <inheritdoc />
        public void RecordSuccess(string? tenantId)
        {
            if (!_states.TryGetValue(Key(tenantId), out var state)) return;

            lock (state)
            {
                state.OpenedUntil = default;
                state.ProbeInFlight = false;
            }
        }

        /// <inheritdoc />
        public void RecordUnavailable(string? tenantId)
        {
            var state = _states.GetOrAdd(Key(tenantId), _ => new State());

            lock (state)
            {
                // No failure threshold, unlike the proxy's breaker. An unreachable secret store is
                // not a flaky upstream to tolerate a few of — it is a platform dependency that is
                // either there or not, and the cost of each attempt is three slots held for the
                // whole timeout. One is enough to stop asking.
                state.ProbeInFlight = false;
                state.OpenedUntil = _time.GetUtcNow().Add(OpenWindow);
            }
        }
    }
}
