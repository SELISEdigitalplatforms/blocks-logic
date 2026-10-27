using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Admission
{
    /// <summary>
    /// The live room check (RUNNER-FLOW step 0): is there CPU and RAM on this machine, right now,
    /// for one more thing? Replaces the counting budget.
    /// <para>
    /// Why: the old budget counted slots, booked memory and start tokens in memory. A count that was
    /// never given back stayed taken until a restart, and on 2026-10-26 the runner refused every run
    /// on an idle machine (CPU 3.5 %, RAM 7 %). Here nothing is counted. Every check reads the
    /// machine again — cores, total RAM, CPU in use, <c>MemAvailable</c> — so a container that is
    /// gone is room at once, and a VM resize applies without a restart.
    /// </para>
    /// <para>
    /// The one bounded limit is <see cref="MaxColdStarts"/> starts at the same time. Each start
    /// holds its place only until it is released or <see cref="ColdStartWindow"/> has passed,
    /// whichever comes first, so even a start that is never released frees its place on its own.
    /// </para>
    /// </summary>
    public sealed class HostRoom
    {
        /// <summary>Cold starts at the same time per runner (RUNNER-FLOW step 10).</summary>
        public const int MaxColdStarts = 3;

        /// <summary>Tests wait while less than this share of the sandbox CPU is free (step 7).</summary>
        internal const double TestMinFreeFraction = 0.30;

        private const long Mb = 1024L * 1024;

        private readonly RunnerOptions _options;
        private readonly IHostSignals _signals;
        private readonly ILogger<HostRoom> _logger;
        private readonly TimeProvider _time;

        private readonly Lock _gate = new();

        /// <summary>When each cold start in flight stops counting, by start. Expired ones are dropped on every check.</summary>
        private readonly Dictionary<ColdStart, long> _coldStarts = [];

        private string _lastRefusal = string.Empty;

        public HostRoom(
            IOptions<RunnerOptions> options,
            IHostSignals signals,
            ILogger<HostRoom> logger,
            TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(signals);
            _options = options.Value;
            _signals = signals;
            _logger = logger;
            _time = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// How long one cold start counts against <see cref="MaxColdStarts"/>: the start boost's own
        /// limit (1 core for ≤ 5 s), after which the sandbox runs at the run CPU like any other.
        /// </summary>
        internal TimeSpan ColdStartWindow =>
            TimeSpan.FromMilliseconds(_options.StartBoostMaxMs > 0 ? _options.StartBoostMaxMs : 5000);

        /// <summary>The last reason a check said no, for the heartbeat. Empty when the last check said yes.</summary>
        public string LastRefusal { get { lock (_gate) { return _lastRefusal; } } }

        /// <summary>The machine now, with the reserve taken off.</summary>
        public RoomSnapshot Read()
        {
            var reading = _signals.Read();
            var reserved = ReservedCores(reading.Cores, _options.ReservedCores);
            var sandboxCores = Math.Max(0, reading.Cores - reserved);
            // The reserve absorbs the runner, Docker and builds first; only what they use beyond it
            // comes out of the sandboxes' share.
            var usedBySandboxes = Math.Max(0, reading.BusyCores - reserved);
            var reservedMemory = _options.ReservedHostMemoryMb * Mb;
            return new RoomSnapshot(
                reading.Cores,
                reserved,
                sandboxCores,
                reading.BusyCores,
                Math.Max(0, sandboxCores - usedBySandboxes),
                reading.AvailableMemoryBytes,
                Math.Max(0, reading.AvailableMemoryBytes - reservedMemory),
                ColdStartsInFlight());
        }

        /// <summary>
        /// The reserve for the runner, Docker and builds, by machine size: up to 4 cores → 1,
        /// 5–8 → 1.5, more than 8 → 2. A configured value (1–2) wins.
        /// </summary>
        internal static double ReservedCores(double cores, double? configured)
        {
            if (configured is { } set) return Math.Clamp(set, 1, 2);
            if (cores <= 4) return 1;
            return cores <= 8 ? 1.5 : 2;
        }

        /// <summary>
        /// Sandboxes this host could run at the run CPU — sandbox cores ÷ 100m. Not a limit on
        /// anything here: it is reported in the heartbeat and is what the fleet's tenant share
        /// (<see cref="FleetCapacity"/>) is a share of.
        /// </summary>
        public int Capacity => CapacityOf(Read());

        internal static int CapacityOf(RoomSnapshot room) =>
            Math.Max(1, (int)(room.SandboxCores * 1000 / Ceilings.CpuMillicores));

        /// <summary>Room for one call on a warm sandbox: the run CPU (100m) free. No RAM — a paused sandbox already holds its own.</summary>
        public bool HasRoomForWarmCall()
        {
            var room = Read();
            return Decide(room.SandboxCpuFree >= RunCpuCores, "CPU", room);
        }

        /// <summary>
        /// Whether a new sandbox could start now (<see cref="TryStartCold"/>'s test), without taking a
        /// place. The run loop asks this before it reads a run that may need a start.
        /// </summary>
        public bool HasRoomForStart(long memoryBytes)
        {
            var room = Read();
            lock (_gate)
            {
                PruneExpiredLocked();
                var refusal = ColdRefusal(room, memoryBytes, _coldStarts.Count);
                if (refusal == RoomRefusal.None)
                {
                    _lastRefusal = string.Empty;
                    return true;
                }
                NoteRefusalLocked(Describe(refusal), room);
                return false;
            }
        }

        /// <summary>
        /// Room for one new sandbox: the start boost CPU and the sandbox's RAM free, and fewer than
        /// <see cref="MaxColdStarts"/> starts in flight. On yes the start's place is taken until the
        /// returned permit is disposed or <see cref="ColdStartWindow"/> passes.
        /// </summary>
        public ColdStart? TryStartCold(long memoryBytes, out RoomRefusal refusal)
        {
            var room = Read();
            lock (_gate)
            {
                PruneExpiredLocked();
                refusal = ColdRefusal(room, memoryBytes, _coldStarts.Count);

                if (refusal != RoomRefusal.None)
                {
                    NoteRefusalLocked(Describe(refusal), room);
                    return null;
                }

                _lastRefusal = string.Empty;
                var start = new ColdStart(this);
                _coldStarts[start] = _time.GetTimestamp() + (long)(ColdStartWindow.TotalSeconds * _time.TimestampFrequency);
                return start;
            }
        }

        /// <summary>
        /// Room for a build: RAM only. What is free after the build's own RAM must still cover the
        /// reserve. Builds take no sandbox CPU — they run on the reserved cores at low priority.
        /// </summary>
        public bool HasRoomForBuild()
        {
            var room = Read();
            return Decide(room.MemoryFreeBytes >= _options.BuildMemoryMb * Mb, "RAM for a build", room);
        }

        /// <summary>A test yields to real traffic: it runs only while at least 30 % of the sandbox CPU is free.</summary>
        public bool HasRoomForTest()
        {
            var room = Read();
            return Decide(room.SandboxCores > 0 && room.SandboxCpuFree / room.SandboxCores >= TestMinFreeFraction,
                "CPU for a test", room);
        }

        /// <summary>The RAM a build needs and does not have now, or 0 — what evicting idle warm sandboxes should free.</summary>
        public long BuildMemoryShortBytes()
        {
            var room = Read();
            return Math.Max(0, (_options.BuildMemoryMb * Mb) - room.MemoryFreeBytes);
        }

        private RoomRefusal ColdRefusal(RoomSnapshot room, long memoryBytes, int inFlight)
        {
            // Starts in flight do not show in /proc yet: what they are about to take is not free.
            if (room.SandboxCpuFree - (inFlight * StartCpuCores) < StartCpuCores) return RoomRefusal.Cpu;
            if (room.MemoryFreeBytes - (inFlight * memoryBytes) < memoryBytes) return RoomRefusal.Memory;
            return inFlight >= MaxColdStarts ? RoomRefusal.ColdStarts : RoomRefusal.None;
        }

        private static double RunCpuCores => Ceilings.CpuMillicores / 1000.0;

        private double StartCpuCores =>
            Math.Max(Ceilings.CpuMillicores, _options.StartBoostMillicores) / 1000.0;

        private int ColdStartsInFlight()
        {
            lock (_gate)
            {
                PruneExpiredLocked();
                return _coldStarts.Count;
            }
        }

        private void PruneExpiredLocked()
        {
            if (_coldStarts.Count == 0) return;
            var now = _time.GetTimestamp();
            foreach (var expired in _coldStarts.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
            {
                _coldStarts.Remove(expired);
            }
        }

        internal void Release(ColdStart start)
        {
            lock (_gate) { _coldStarts.Remove(start); }
        }

        private bool Decide(bool fits, string what, RoomSnapshot room)
        {
            lock (_gate)
            {
                if (fits) _lastRefusal = string.Empty;
                else NoteRefusalLocked($"no {what}", room);
            }
            return fits;
        }

        private static string Describe(RoomRefusal refusal) => refusal switch
        {
            RoomRefusal.Cpu => "no CPU for a start",
            RoomRefusal.Memory => "no RAM for a start",
            _ => $"{MaxColdStarts} starts already in flight",
        };

        private void NoteRefusalLocked(string reason, RoomSnapshot room)
        {
            var text = FormattableString.Invariant(
                $"{reason} (sandbox CPU free {room.SandboxCpuFree:0.00}/{room.SandboxCores:0.##}, RAM free {room.MemoryFreeBytes / Mb} MB, starts {room.ColdStarts})");
            if (text == _lastRefusal) return;
            _lastRefusal = text;
            _logger.LogDebug("No room: {Reason}", text);
        }
    }

    /// <summary>One reading of the room, after the reserve. Also what the heartbeat shows.</summary>
    /// <param name="Cores">Usable cores on the machine.</param>
    /// <param name="ReservedCores">Kept for the runner, Docker and builds.</param>
    /// <param name="SandboxCores">Cores for sandboxes: <paramref name="Cores"/> − <paramref name="ReservedCores"/>.</param>
    /// <param name="BusyCores">CPU in use on the whole machine.</param>
    /// <param name="SandboxCpuFree">Sandbox cores not in use now.</param>
    /// <param name="AvailableMemoryBytes"><c>MemAvailable</c>.</param>
    /// <param name="MemoryFreeBytes"><c>MemAvailable</c> − the RAM reserve.</param>
    /// <param name="ColdStarts">Starts in flight now.</param>
    public readonly record struct RoomSnapshot(
        double Cores,
        double ReservedCores,
        double SandboxCores,
        double BusyCores,
        double SandboxCpuFree,
        long AvailableMemoryBytes,
        long MemoryFreeBytes,
        int ColdStarts);

    /// <summary>Why <see cref="HostRoom.TryStartCold"/> said no.</summary>
    public enum RoomRefusal
    {
        None,

        /// <summary>Not enough free sandbox CPU for the start boost.</summary>
        Cpu,

        /// <summary>Not enough RAM — the one refusal that removing idle warm sandboxes can fix.</summary>
        Memory,

        /// <summary><see cref="HostRoom.MaxColdStarts"/> starts are already in flight.</summary>
        ColdStarts,
    }

    /// <summary>
    /// One cold start's place among <see cref="HostRoom.MaxColdStarts"/>. Disposing gives it back;
    /// not disposing only delays that until <see cref="HostRoom.ColdStartWindow"/> has passed.
    /// </summary>
    public sealed class ColdStart : IDisposable
    {
        private readonly HostRoom _room;
        private int _released;

        internal ColdStart(HostRoom room) => _room = room;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) _room.Release(this);
        }
    }
}
