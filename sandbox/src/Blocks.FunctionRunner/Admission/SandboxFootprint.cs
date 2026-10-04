namespace Blocks.FunctionRunner.Admission
{
    /// <summary>
    /// What a sandbox on this host actually costs, measured rather than assumed.
    /// <para>
    /// Admission used to reserve a run's whole memory <i>limit</i>. That is provably safe and
    /// badly wasteful: a function is given 128 MB and typically peaks nearer 60, so a host sized
    /// on limits runs at about half the density it could. The runner already measures the real
    /// figure on every run (<c>SandboxResult.PeakMemoryBytes</c>, from Docker's stats stream), so
    /// the budget reserves the observed p95 instead — high enough that the overwhelming majority
    /// of runs fit inside their reservation, low enough to recover most of the waste.
    /// </para>
    /// <para>
    /// p95 and not the mean: the mean is dragged down by the many cheap runs and would under-
    /// reserve exactly when a batch of expensive ones arrives together. The remaining tail is
    /// what the host's memory floor and the pressure controller are for — this number decides
    /// density, it is not the thing keeping the host alive.
    /// </para>
    /// </summary>
    public sealed class SandboxFootprint
    {
        /// <summary>
        /// Samples kept. A few hundred is enough for a stable p95 and short enough that the
        /// estimate follows a change in what this host is actually running, rather than averaging
        /// over yesterday's workload.
        /// </summary>
        internal const int WindowSize = 256;

        /// <summary>
        /// Samples required before the estimate is used at all. Below this the budget keeps
        /// reserving full limits — a cold runner must not widen its own admission on the evidence
        /// of three cheap runs.
        /// </summary>
        internal const int MinimumSamples = 32;

        private readonly Lock _gate = new();
        private readonly long[] _samples = new long[WindowSize];
        private int _count;
        private int _next;

        /// <summary>Records one completed run's peak. Ignores absent or nonsensical readings.</summary>
        public void Record(long? peakMemoryBytes)
        {
            if (peakMemoryBytes is not > 0) return;

            lock (_gate)
            {
                _samples[_next] = peakMemoryBytes.Value;
                _next = (_next + 1) % WindowSize;
                if (_count < WindowSize) _count++;
            }
        }

        /// <summary>How many runs have been measured, capped at the window.</summary>
        public int SampleCount { get { lock (_gate) { return _count; } } }

        /// <summary>
        /// What to reserve for one sandbox: the observed p95, or <paramref name="limitBytes"/>
        /// while there is not yet enough evidence. Never returns more than the limit — a run
        /// cannot exceed its own cgroup, so a p95 above it would mean the samples are wrong.
        /// </summary>
        public long ReservationFor(long limitBytes)
        {
            long[] snapshot;
            int count;

            lock (_gate)
            {
                if (_count < MinimumSamples) return limitBytes;
                snapshot = new long[_count];
                Array.Copy(_samples, snapshot, _count);
                count = _count;
            }

            Array.Sort(snapshot);
            // Index of the p95 sample, clamped into the array for small windows.
            var index = Math.Min(count - 1, (int)Math.Ceiling(count * 0.95) - 1);
            var p95 = snapshot[Math.Max(0, index)];

            return Math.Min(limitBytes, Math.Max(1, p95));
        }
    }
}
