using System.Globalization;

namespace Blocks.FunctionRunner.Admission
{
    /// <summary>
    /// One reading of how hard this host is currently working.
    /// <para>
    /// The pressure figures are Linux PSI (<c>/proc/pressure/*</c>), in percent of the last ten
    /// seconds during which at least one task was stalled waiting for the resource. They are the
    /// reason this design needs no invented efficiency factor: rather than assuming what fraction
    /// of a machine is usable, the kernel is asked whether anything is actually waiting.
    /// </para>
    /// </summary>
    /// <param name="CpuStalledPercent">PSI cpu <c>some avg10</c>. 0 means nothing waited for CPU.</param>
    /// <param name="MemoryStalledPercent">PSI memory <c>some avg10</c>: reclaim is costing time.</param>
    /// <param name="MemoryFullStalledPercent">PSI memory <c>full avg10</c>: everything stalled at once.</param>
    /// <param name="AvailableMemoryBytes">
    /// <c>MemAvailable</c> — the kernel's own estimate of what can still be allocated without
    /// swapping. Not "free": it counts reclaimable cache, which is what makes it the right number.
    /// </param>
    public readonly record struct HostSignalSample(
        double CpuStalledPercent,
        double MemoryStalledPercent,
        double MemoryFullStalledPercent,
        long AvailableMemoryBytes);

    /// <summary>What the admission controller is allowed to know about its host.</summary>
    public interface IHostSignals
    {
        /// <summary>Usable cores, from the cgroup quota where one applies.</summary>
        double Cores { get; }

        /// <summary>Memory ceiling, from the cgroup where one applies.</summary>
        long TotalMemoryBytes { get; }

        /// <summary>Reads the current pressure. Never throws; unreadable signals read as calm.</summary>
        HostSignalSample Sample();
    }

    /// <summary>
    /// <see cref="IHostSignals"/> over <c>/proc</c> and cgroup v2.
    /// <para>
    /// Everything here is read rather than configured, and everything degrades to a safe answer:
    /// an unreadable pressure file reads as "no stall", which on its own would let the controller
    /// grow — so growth is additionally gated on real demand and bounded by the memory arithmetic,
    /// which is derived from figures that do not depend on PSI being present.
    /// </para>
    /// </summary>
    public sealed class ProcHostSignals : IHostSignals
    {
        private const string CpuPressurePath = "/proc/pressure/cpu";
        private const string MemoryPressurePath = "/proc/pressure/memory";
        private const string MemInfoPath = "/proc/meminfo";
        private const string CgroupCpuMaxPath = "/sys/fs/cgroup/cpu.max";
        private const string CgroupMemoryMaxPath = "/sys/fs/cgroup/memory.max";

        public ProcHostSignals()
        {
            Cores = ReadCores();
            TotalMemoryBytes = ReadTotalMemory();
        }

        public double Cores { get; }

        public long TotalMemoryBytes { get; }

        public HostSignalSample Sample() => new(
            ReadPressure(CpuPressurePath, "some"),
            ReadPressure(MemoryPressurePath, "some"),
            ReadPressure(MemoryPressurePath, "full"),
            ReadAvailableMemory());

        /// <summary>
        /// Cores the runner may actually use. A cgroup quota wins over the machine's core count,
        /// so a containerised runner budgets against its own slice rather than the host's.
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

            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }

        /// <summary>
        /// MemAvailable in bytes, or <see cref="long.MaxValue"/> when it cannot be read — an
        /// unknown figure must not be mistaken for a host that is out of memory, because that
        /// would wedge admission at one sandbox forever. The static memory bound still applies.
        /// </summary>
        private static long ReadAvailableMemory()
        {
            try
            {
                foreach (var line in File.ReadLines(MemInfoPath))
                {
                    if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal)) continue;

                    var digits = line.AsSpan("MemAvailable:".Length).Trim();
                    var end = digits.IndexOf(' ');
                    if (end > 0) digits = digits[..end];

                    if (long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb))
                    {
                        return kb * 1024;
                    }
                    break;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return long.MaxValue;
        }

        /// <summary>Parses one PSI line, e.g. <c>some avg10=0.00 avg60=0.01 …</c>.</summary>
        internal static double ParsePressure(string content, string kind)
        {
            foreach (var line in content.Split('\n'))
            {
                if (!line.StartsWith(kind, StringComparison.Ordinal)) continue;

                const string marker = "avg10=";
                var at = line.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0) return 0;

                var rest = line.AsSpan(at + marker.Length);
                var end = rest.IndexOf(' ');
                if (end > 0) rest = rest[..end];

                return double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0;
            }

            return 0;
        }

        private static double ReadPressure(string path, string kind)
        {
            try
            {
                return ParsePressure(File.ReadAllText(path), kind);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            // PSI absent (a kernel without CONFIG_PSI). Reads as calm; growth is still bounded by
            // the memory arithmetic and by demand, so this degrades to capacity-based admission.
            return 0;
        }
    }
}
