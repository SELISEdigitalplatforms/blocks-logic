using Docker.DotNet.Models;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// Folds the samples of a container's live stats stream into the two numbers the run record
    /// keeps: peak memory and total CPU time.
    /// <para>
    /// Both are kept as a running maximum, and for the same reason: the stream is not a clean
    /// series of readings taken while the container runs. Docker emits a sample the moment the
    /// stream is attached — before the container has done anything — and one or more <b>after</b>
    /// it exits, once its cgroup is gone; those carry zeroes rather than the final tally. Taking
    /// the latest sample therefore reports zero for every run that ends normally, which is what
    /// used to reach the UI as a flat "CPU TIME 0 ms" next to a perfectly real duration.
    /// </para>
    /// <para>
    /// A maximum is the right fold for both counters even so: memory has to be tracked this way
    /// because cgroup v2 exposes only instantaneous usage (there is no equivalent of cgroup v1's
    /// <c>memory.max_usage_in_bytes</c>), and CPU time is a cumulative counter that only ever
    /// climbs while the container lives, so its high-water mark <i>is</i> the run's total.
    /// </para>
    /// <para>
    /// Samples arrive on <see cref="System.Progress{T}"/> callbacks, which are posted to the
    /// thread pool and may overlap, so every update is a compare-and-swap rather than a
    /// read-then-write.
    /// </para>
    /// </summary>
    internal sealed class SandboxStatsAccumulator
    {
        private long _peakMemoryBytes = -1;
        private long _peakCpuUsageNs = -1;

        /// <summary>
        /// The highest memory usage observed, or null when no sample carried one — a run that
        /// finished before Docker's first (~1s) sample, or a stats stream that never attached.
        /// </summary>
        public long? PeakMemoryBytes
        {
            get
            {
                var peak = Interlocked.Read(ref _peakMemoryBytes);
                return peak > 0 ? peak : null;
            }
        }

        /// <summary>
        /// Total CPU time consumed over the whole run, in milliseconds; null under the same
        /// conditions as <see cref="PeakMemoryBytes"/>, plus the zero readings Docker brackets
        /// the stream with, which say only that the container had not started or had already
        /// gone. Any CPU time actually measured rounds <i>up</i> to at least 1 ms — over-reporting
        /// a sub-millisecond run by a fraction of a millisecond is a far better trade than
        /// printing the "0 ms" that means "we never looked".
        /// </summary>
        public long? CpuUsageMs
        {
            get
            {
                var cpuNs = Interlocked.Read(ref _peakCpuUsageNs);
                return cpuNs > 0 ? (cpuNs + 999_999) / 1_000_000 : null;
            }
        }

        /// <summary>Folds one sample in. Safe to call from overlapping callbacks.</summary>
        public void Add(ContainerStatsResponse sample)
        {
            ArgumentNullException.ThrowIfNull(sample);

            if (sample.MemoryStats is { } memory)
            {
                RaiseTo(ref _peakMemoryBytes, unchecked((long)memory.Usage));
            }

            if (sample.CPUStats?.CPUUsage is { } cpu)
            {
                RaiseTo(ref _peakCpuUsageNs, unchecked((long)cpu.TotalUsage));
            }
        }

        /// <summary>
        /// Raises <paramref name="target"/> to <paramref name="candidate"/> if that is higher.
        /// Negative candidates — which a counter past <see cref="long.MaxValue"/> would produce
        /// through the unchecked cast, roughly 292 years of CPU time away — lose to the initial
        /// -1 and are discarded rather than passed on as a nonsense reading.
        /// </summary>
        private static void RaiseTo(ref long target, long candidate)
        {
            long observed;
            do
            {
                observed = Interlocked.Read(ref target);
                if (candidate <= observed) return;
            }
            while (Interlocked.CompareExchange(ref target, candidate, observed) != observed);
        }
    }
}
