using Blocks.FunctionRunner.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Admission
{
    /// <summary>
    /// Decides whether this host can take on another sandbox, and how many it should be running.
    /// <para>
    /// Refusal is not rejection. An entry this host declines stays pending in the stream and is
    /// picked up when capacity frees — by this runner or another. Nothing is ever failed for
    /// volume, which is the promise in <c>plan/DECISIONS.md</c>.
    /// </para>
    /// <para>
    /// The slot count is discovered, not configured. A hand-set ceiling has to be guessed for the
    /// smallest host anyone might deploy on, so every larger host then runs under capacity — and
    /// the guess is wrong in both directions as soon as the workload changes. Instead:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Capacity</b> comes from the cgroup and <c>/proc</c>: cores, the memory limit, and
    /// <c>MemAvailable</c>.</item>
    /// <item><b>Cost</b> comes from <see cref="SandboxFootprint"/> — what runs on this host have
    /// actually peaked at, not what they were permitted.</item>
    /// <item><b>Headroom</b> comes from Linux PSI: the kernel is asked whether anything is
    /// actually stalling, rather than an efficiency factor being invented.</item>
    /// </list>
    /// <para>
    /// The one judgement that is not measured is the setpoint — how much stall is acceptable
    /// before the host is considered full. It is expressed in the unit PSI reports (percent of
    /// the last ten seconds during which something waited), so it can be checked against a real
    /// host, and a wrong value self-corrects into a slightly different equilibrium rather than a
    /// silent cap.
    /// </para>
    /// <para>
    /// Growth is deliberately timid and shrinking is not: one slot at a time, and only while the
    /// host is genuinely saturated, against a quarter of the slots at once the moment memory
    /// stalls. Over-admitting costs far more than under-admitting, because the way this host
    /// fails is the OOM killer, and the process it must never pick is this one.
    /// </para>
    /// </summary>
    public sealed class HostBudget
    {
        // ---- control setpoints ------------------------------------------------------
        // PSI "some avg10": the percentage of the last ten seconds during which at least one
        // task was stalled waiting for the resource. Zero on an idle host; small and non-zero
        // on a healthily busy one; large when work is queueing behind the CPU.

        /// <summary>Below this much CPU stall the host still has room, so grow.</summary>
        internal const double CpuStallGrowBelowPercent = 5.0;

        /// <summary>Above this much CPU stall the host is past its useful point, so shrink.</summary>
        internal const double CpuStallShrinkAbovePercent = 20.0;

        /// <summary>
        /// Any sustained memory stall shrinks the host. Memory pressure does not degrade
        /// gracefully the way CPU contention does — it ends in the OOM killer.
        /// </summary>
        internal const double MemoryStallShrinkAbovePercent = 1.0;

        /// <summary>Fraction of slots given up when memory complains.</summary>
        internal const double ShrinkFraction = 0.25;

        /// <summary>How often the signals are re-read. Shorter than PSI's own 10 s window.</summary>
        internal static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(2);

        private readonly RunnerOptions _options;
        private readonly IHostSignals _signals;
        private readonly SandboxFootprint _footprint;
        private readonly ILogger<HostBudget> _logger;
        private readonly TimeProvider _time;

        private readonly Lock _gate = new();
        private int _active;
        private long _committedMemoryBytes;
        private int _slots;
        private long _lastSampleTicks;
        private HostSignalSample _lastSample;

        public HostBudget(
            IOptions<RunnerOptions> options,
            IHostSignals signals,
            SandboxFootprint footprint,
            ILogger<HostBudget> logger,
            TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(signals);

            _options = options.Value;
            _signals = signals;
            _footprint = footprint;
            _logger = logger;
            _time = timeProvider ?? TimeProvider.System;

            _slots = InitialSlots();
            _lastSampleTicks = long.MinValue;

            _logger.LogInformation(
                "Admission starts at {Slots} sandboxes ({Mode}); host has {Cores:0.##} cores and {MemoryMb} MB",
                _slots,
                _options.MaxActiveSandboxes is null ? "auto" : "pinned by configuration",
                _signals.Cores,
                _signals.TotalMemoryBytes / 1024 / 1024);
        }

        public int Active { get { lock (_gate) { return _active; } } }

        /// <summary>The slot count in force right now — moves with the host unless pinned.</summary>
        public int Capacity { get { lock (_gate) { return _slots; } } }

        /// <summary>Bytes currently reserved across live sandboxes.</summary>
        public long CommittedMemoryBytes { get { lock (_gate) { return _committedMemoryBytes; } } }

        /// <summary>
        /// Tries to reserve capacity for a sandbox of the given size.
        /// </summary>
        /// <param name="memoryBytes">The run's memory <i>limit</i>; what is reserved may be less.</param>
        /// <returns>A disposable that releases the reservation, or null when the host is full.</returns>
        public IDisposable? TryReserve(long memoryBytes)
        {
            lock (_gate)
            {
                Retune();

                if (_active >= _slots) return null;

                // What this sandbox is expected to cost, from measurement where there is any.
                var reserve = _footprint.ReservationFor(memoryBytes);
                var floor = (long)_options.ReservedHostMemoryMb * 1024 * 1024;

                // Two independent memory bounds, and both must hold. The first is arithmetic over
                // what this runner has promised; the second is the kernel's own view, which also
                // covers everything the runner did not promise — dockerd, the gVisor sentries,
                // page cache the host actually needs.
                if (_committedMemoryBytes + reserve > _signals.TotalMemoryBytes - floor) return null;
                if (_lastSample.AvailableMemoryBytes - reserve < floor) return null;

                _active++;
                _committedMemoryBytes += reserve;
                return new Reservation(this, reserve);
            }
        }

        /// <summary>
        /// Folds one finished run's measured peak into the footprint estimate. Called by the run
        /// processor; every sample makes the next reservation a little less of an over-estimate.
        /// </summary>
        public void Observe(long? peakMemoryBytes) => _footprint.Record(peakMemoryBytes);

        /// <summary>
        /// Moves the slot count toward what the host can currently sustain. Called under the lock
        /// on every admission decision and throttled to <see cref="SampleInterval"/>, so it costs
        /// three small /proc reads a couple of times a second at most.
        /// </summary>
        private void Retune()
        {
            var now = _time.GetTimestamp();
            if (_lastSampleTicks != long.MinValue &&
                _time.GetElapsedTime(_lastSampleTicks, now) < SampleInterval)
            {
                return;
            }

            _lastSampleTicks = now;
            var sample = _signals.Sample();
            _lastSample = sample;

            // A pinned slot count belongs to the operator, so the loop below does not run. The
            // sample above is still taken: the MemAvailable bound in TryReserve is a safety check
            // on the host, not part of the sizing loop, and it has to hold either way — more so
            // under a pinned count, which is a guess nothing corrects.
            if (_options.MaxActiveSandboxes is not null) return;

            var ceiling = MemoryCeiling();
            var before = _slots;

            if (sample.MemoryFullStalledPercent > 0 ||
                sample.MemoryStalledPercent > MemoryStallShrinkAbovePercent)
            {
                // Reclaim is already costing time. Give back a quarter at once rather than
                // creeping down while the host thrashes.
                _slots = Math.Max(1, _slots - Math.Max(1, (int)(_slots * ShrinkFraction)));
            }
            else if (sample.CpuStalledPercent > CpuStallShrinkAbovePercent)
            {
                _slots = Math.Max(1, _slots - 1);
            }
            else if (sample.CpuStalledPercent < CpuStallGrowBelowPercent && _active >= _slots)
            {
                // Only when the host is actually full does another slot mean anything; growing
                // on an idle host would just raise a number nobody is asking for.
                _slots = Math.Min(ceiling, _slots + 1);
            }

            // Whatever the loop decided, never stand above what memory can hold.
            _slots = Math.Clamp(_slots, 1, Math.Max(1, ceiling));

            if (_slots != before)
            {
                _logger.LogInformation(
                    "Admission {Direction} {Before} -> {After} (cpu stall {Cpu:0.0}%, memory stall {Mem:0.0}%/{Full:0.0}%, {AvailableMb} MB available)",
                    _slots > before ? "grew" : "shrank", before, _slots,
                    sample.CpuStalledPercent, sample.MemoryStalledPercent, sample.MemoryFullStalledPercent,
                    sample.AvailableMemoryBytes == long.MaxValue ? -1 : sample.AvailableMemoryBytes / 1024 / 1024);
            }
        }

        /// <summary>
        /// The most sandboxes this host's memory can hold, at the currently believed cost each.
        /// This is the bound the pressure loop may never grow past.
        /// </summary>
        private int MemoryCeiling()
        {
            var floor = (long)_options.ReservedHostMemoryMb * 1024 * 1024;
            var usable = _signals.TotalMemoryBytes - floor;
            if (usable <= 0) return 1;

            var each = _footprint.ReservationFor(Contracts.Ceilings.MemoryBytes);
            return (int)Math.Max(1, usable / Math.Max(1, each));
        }

        /// <summary>
        /// Where the controller starts on a host it knows nothing about: the CPU-derived count,
        /// bounded by memory reserved at full limits. Both are conservative and provable — the
        /// first real samples widen it within a minute or two of traffic.
        /// </summary>
        private int InitialSlots()
        {
            if (_options.MaxActiveSandboxes is { } pinned) return pinned;

            var cpuSlots = (int)Math.Max(1, _signals.Cores * 1000 / Contracts.Ceilings.CpuMillicores);
            return Math.Max(1, Math.Min(cpuSlots, MemoryCeiling()));
        }

        private void Release(long memoryBytes)
        {
            lock (_gate)
            {
                _active = Math.Max(0, _active - 1);
                _committedMemoryBytes = Math.Max(0, _committedMemoryBytes - memoryBytes);
            }
        }

        private sealed class Reservation : IDisposable
        {
            private readonly HostBudget _budget;
            private readonly long _memoryBytes;
            private bool _released;

            public Reservation(HostBudget budget, long memoryBytes)
            {
                _budget = budget;
                _memoryBytes = memoryBytes;
            }

            public void Dispose()
            {
                if (_released) return;
                _released = true;
                _budget.Release(_memoryBytes);
            }
        }
    }
}
