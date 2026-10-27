using System.Globalization;

namespace Blocks.FunctionRunner.Admission
{
    /// <summary>
    /// One live reading of the machine. Nothing in it is remembered from an earlier decision: it is
    /// what the kernel says right now, so a VM resize or a leaked count can never make it wrong for
    /// longer than one reading (RUNNER-FLOW step 0).
    /// </summary>
    /// <param name="Cores">Usable cores: the cgroup quota where one applies, else the online CPUs.</param>
    /// <param name="TotalMemoryBytes">Memory ceiling: the cgroup limit where one applies, else <c>MemTotal</c>.</param>
    /// <param name="AvailableMemoryBytes">
    /// <c>MemAvailable</c> — what can still be allocated without swapping. Not "free": it counts
    /// reclaimable cache, which is what makes it the right number.
    /// </param>
    /// <param name="BusyCores">CPU in use on the whole machine, in cores, over the last short window.</param>
    public readonly record struct HostReading(
        double Cores,
        long TotalMemoryBytes,
        long AvailableMemoryBytes,
        double BusyCores);

    /// <summary>What the room check is allowed to know about its host.</summary>
    public interface IHostSignals
    {
        /// <summary>Reads the machine now. Never throws; an unreadable figure reads as the safe answer.</summary>
        HostReading Read();
    }

    /// <summary>
    /// <see cref="IHostSignals"/> over <c>/proc</c> and cgroup v2, read again on every call.
    /// <para>
    /// CPU in use comes from <c>/proc/stat</c>: the busy share of all CPU time between this reading
    /// and the previous one. The previous reading is the only thing kept, and only to take the
    /// difference — it is replaced on every call, so it cannot drift or leak.
    /// </para>
    /// </summary>
    public sealed class ProcHostSignals : IHostSignals
    {
        private const string StatPath = "/proc/stat";
        private const string MemInfoPath = "/proc/meminfo";
        private const string CgroupCpuMaxPath = "/sys/fs/cgroup/cpu.max";
        private const string CgroupMemoryMaxPath = "/sys/fs/cgroup/memory.max";

        /// <summary>
        /// The shortest window a CPU figure is taken over. Two checks a few milliseconds apart would
        /// otherwise divide almost nothing by almost nothing; inside this window the last figure stands.
        /// </summary>
        internal static readonly TimeSpan MinCpuWindow = TimeSpan.FromMilliseconds(250);

        private readonly Lock _gate = new();
        private readonly TimeProvider _time;
        private CpuTimes? _last;
        private long _lastTicks;
        private double _lastBusyFraction;

        public ProcHostSignals() : this(TimeProvider.System)
        {
        }

        internal ProcHostSignals(TimeProvider time)
        {
            _time = time;
        }

        public HostReading Read()
        {
            var cores = ReadCores();
            var total = ReadTotalMemory();
            // Unreadable MemAvailable (no /proc) reads as "all of it": an unknown figure must not
            // refuse every start for ever. On Linux it is always there.
            var available = ReadMemInfo("MemAvailable:") ?? total;
            return new HostReading(cores, total, Math.Min(available, total), BusyFraction() * cores);
        }

        /// <summary>The busy share (0–1) of all CPUs since the previous reading.</summary>
        private double BusyFraction()
        {
            var now = ReadCpuTimes();
            if (now is null) return 0;

            lock (_gate)
            {
                var ticks = _time.GetTimestamp();
                if (_last is not null && _time.GetElapsedTime(_lastTicks, ticks) < MinCpuWindow)
                {
                    return _lastBusyFraction;
                }

                if (_last is { } before)
                {
                    _lastBusyFraction = BusyBetween(before, now.Value);
                }
                _last = now;
                _lastTicks = ticks;
                return _lastBusyFraction;
            }
        }

        /// <summary>Total and idle CPU time from the first (all-CPU) line of <c>/proc/stat</c>.</summary>
        internal readonly record struct CpuTimes(long Total, long Idle);

        /// <summary>The busy share between two readings; 0 when no time passed.</summary>
        internal static double BusyBetween(CpuTimes before, CpuTimes after)
        {
            var total = after.Total - before.Total;
            var idle = after.Idle - before.Idle;
            if (total <= 0) return 0;
            return Math.Clamp((double)(total - idle) / total, 0, 1);
        }

        /// <summary>
        /// Parses the aggregate <c>cpu</c> line: user nice system idle iowait irq softirq steal …
        /// Idle is idle + iowait (a CPU waiting on disk can still run a sandbox).
        /// </summary>
        internal static CpuTimes? ParseCpuLine(string line)
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal)) return null;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) return null;

            long total = 0;
            long idle = 0;
            // Guest time (fields 9–10) is already inside user/nice, so it is not added twice.
            for (var i = 1; i < Math.Min(parts.Length, 9); i++)
            {
                if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return null;
                total += value;
                if (i is 4 or 5) idle += value;
            }
            return new CpuTimes(total, idle);
        }

        private static CpuTimes? ReadCpuTimes()
        {
            try
            {
                foreach (var line in File.ReadLines(StatPath))
                {
                    return ParseCpuLine(line);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        /// <summary>
        /// Cores the runner may use, read now: a cgroup quota wins over the machine's count, so a
        /// containerised runner plans against its own slice.
        /// </summary>
        private static double ReadCores()
        {
            try
            {
                // "max 100000" (no limit) or "200000 100000" (2 cores).
                var parts = File.ReadAllText(CgroupCpuMaxPath).Trim().Split(' ');
                if (parts.Length == 2 &&
                    long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quota) &&
                    long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var period) &&
                    quota > 0 && period > 0)
                {
                    return (double)quota / period;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return OnlineCpus();
        }

        /// <summary>The per-CPU lines of <c>/proc/stat</c>, so a hot-added CPU counts without a restart.</summary>
        private static int OnlineCpus()
        {
            try
            {
                var count = File.ReadLines(StatPath)
                    .Count(l => l.StartsWith("cpu", StringComparison.Ordinal) && l.Length > 3 && char.IsAsciiDigit(l[3]));
                if (count > 0) return count;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Environment.ProcessorCount;
        }

        private static long ReadTotalMemory()
        {
            try
            {
                var text = File.ReadAllText(CgroupMemoryMaxPath).Trim();
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) && limit > 0)
                {
                    return limit;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return ReadMemInfo("MemTotal:") ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }

        private static long? ReadMemInfo(string field)
        {
            try
            {
                foreach (var line in File.ReadLines(MemInfoPath))
                {
                    if (line.StartsWith(field, StringComparison.Ordinal)) return ParseMemInfoKb(line, field);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        /// <summary>Parses one <c>/proc/meminfo</c> line, e.g. <c>MemAvailable:  123456 kB</c>, into bytes.</summary>
        internal static long? ParseMemInfoKb(string line, string field)
        {
            if (!line.StartsWith(field, StringComparison.Ordinal)) return null;
            var digits = line.AsSpan(field.Length).Trim();
            var end = digits.IndexOf(' ');
            if (end > 0) digits = digits[..end];
            return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb) ? kb * 1024 : null;
        }
    }
}
