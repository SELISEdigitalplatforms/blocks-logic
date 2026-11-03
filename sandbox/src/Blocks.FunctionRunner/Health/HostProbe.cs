using System.Globalization;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>
    /// Reads the machine's own figures from <c>/proc</c> and the Docker data filesystem.
    /// <para>
    /// Holds the previous CPU reading, because CPU load is a difference between two points in
    /// time; the first sample after a start therefore has no CPU figure.
    /// </para>
    /// </summary>
    public sealed class HostProbe
    {
        private const string DockerRoot = "/var/lib/docker";
        private readonly string _procRoot;
        private CpuTimes? _previousCpu;

        public HostProbe(string procRoot = "/proc")
        {
            _procRoot = procRoot;
        }

        public HostStats Read()
        {
            long? Mb(long kb) => kb / 1024;
            var mem = ReadOrNull(() => MemInfo.Parse(File.ReadLines(Path.Combine(_procRoot, "meminfo"))));
            return new HostStats(
                mem is null ? null : Mb(mem.TotalKb),
                mem is null ? null : Mb(mem.AvailableKb),
                mem is null ? null : Mb(mem.AnonKb),
                mem is null ? null : Mb(mem.CacheKb),
                mem is null ? null : Mb(mem.SwapTotalKb),
                mem is null ? null : Mb(mem.SwapTotalKb - mem.SwapFreeKb),
                ReadCpuPercent(),
                ReadLoad1(),
                ReadDiskPercent());
        }

        private double? ReadCpuPercent()
        {
            var now = ReadOrNull(() => File.ReadLines(Path.Combine(_procRoot, "stat")).Select(CpuTimes.Parse).FirstOrDefault(t => t is not null));
            var before = _previousCpu;
            _previousCpu = now ?? before;
            return before is { } b && now is { } n ? CpuTimes.Percent(b, n) : null;
        }

        private double? ReadLoad1()
        {
            var text = ReadOrNull(() => File.ReadAllText(Path.Combine(_procRoot, "loadavg")));
            var first = text?.Split(' ', 2)[0];
            return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var load) ? load : null;
        }

        /// <summary>
        /// Share of the filesystem holding Docker's data that is used. <c>/var/lib/docker</c> is
        /// root-only, so for the non-root runner the statfs call can be refused; its parent is on
        /// the same filesystem on every layout this runner is deployed with, so it is the fallback.
        /// </summary>
        private static double? ReadDiskPercent()
        {
            foreach (var path in (string[])[DockerRoot, "/var/lib", "/"])
            {
                try
                {
                    var drive = new DriveInfo(path);
                    if (drive.TotalSize <= 0) continue;
                    return 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // Not readable from this user: try the next path.
                }
            }
            return null;
        }

        private static T? ReadOrNull<T>(Func<T> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return default; // Unreadable means "unknown", never a failed sample.
            }
        }
    }
}
