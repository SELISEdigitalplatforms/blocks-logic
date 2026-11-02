using System.Globalization;
using System.Text.RegularExpressions;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>A running containerd-shim process: its pid, the container id on its command line and when it started.</summary>
    public sealed record ShimProcess(int Pid, string? ContainerId, DateTimeOffset Started);

    /// <summary>
    /// Finds containerd shims and decides which ones belong to no container.
    /// <para>
    /// A shim outlives its container when the container's process never exits — the bug that left
    /// 2652 of them (11.7 GB) on a host unnoticed for days. Only root can end them, so both the
    /// runner's recorder (counting) and <c>fnctl police</c> (ending) use this one definition.
    /// </para>
    /// <para>
    /// The rule is deliberately one-sided: a shim is an orphan only when its id could be read and
    /// Docker (asked with <c>All=true</c>, so stopped and paused containers count) does not list
    /// it. Anything unreadable is left alone.
    /// </para>
    /// </summary>
    public static partial class ShimScanner
    {
        private const string ShimPath = "/usr/bin/containerd-shim";
        private const int TicksPerSecond = 100; // USER_HZ, fixed at 100 on Linux.

        [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
        private static partial Regex ContainerIdPattern();

        /// <summary>
        /// Reads a <c>/proc/&lt;pid&gt;/cmdline</c> (NUL-separated). Returns false when the process
        /// is not a containerd shim; <paramref name="containerId"/> is null when it is one but has
        /// no readable <c>-id</c>.
        /// </summary>
        public static bool TryParseCommandLine(string cmdline, out string? containerId)
        {
            containerId = null;
            var args = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0 || !args[0].StartsWith(ShimPath, StringComparison.Ordinal)) return false;

            for (var i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "-id" && ContainerIdPattern().IsMatch(args[i + 1]))
                {
                    containerId = args[i + 1];
                    break;
                }
            }
            return true;
        }

        /// <summary>
        /// The shims whose container Docker does not list and which are at least <paramref name="minAge"/> old.
        /// </summary>
        public static IReadOnlyList<ShimProcess> Orphans(
            IEnumerable<ShimProcess> shims, ISet<string> dockerIds, DateTimeOffset now, TimeSpan minAge) =>
            shims
                .Where(s => s.ContainerId is not null
                    && !dockerIds.Contains(s.ContainerId)
                    && now - s.Started >= minAge)
                .ToList();

        /// <summary>Process start from <c>/proc/&lt;pid&gt;/stat</c> field 22 (ticks since boot) plus the boot time.</summary>
        public static DateTimeOffset? ParseStartTime(string stat, DateTimeOffset bootTime)
        {
            // The command name is in parentheses and may hold spaces; fields count from after the last ')'.
            var close = stat.LastIndexOf(')');
            if (close < 0) return null;
            var fields = stat[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // fields[0] is field 3 (state), so field 22 is fields[19].
            if (fields.Length <= 19
                || !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            {
                return null;
            }
            return bootTime.AddSeconds((double)ticks / TicksPerSecond);
        }

        /// <summary>Every shim running now. Processes that vanish while being read are skipped.</summary>
        public static IReadOnlyList<ShimProcess> Scan(string procRoot = "/proc")
        {
            var bootTime = ReadBootTime(procRoot);
            var shims = new List<ShimProcess>();
            if (bootTime is null) return shims;

            foreach (var dir in Directory.EnumerateDirectories(procRoot))
            {
                if (!int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
                var shim = TryRead(dir, pid, bootTime.Value);
                if (shim is not null) shims.Add(shim);
            }
            return shims;
        }

        /// <summary>Reads one process again; null if it is gone or no longer a shim. Used to re-check right before a kill.</summary>
        public static ShimProcess? TryReadPid(int pid, string procRoot = "/proc")
        {
            var bootTime = ReadBootTime(procRoot);
            return bootTime is null ? null : TryRead(Path.Combine(procRoot, pid.ToString(CultureInfo.InvariantCulture)), pid, bootTime.Value);
        }

        private static ShimProcess? TryRead(string dir, int pid, DateTimeOffset bootTime)
        {
            try
            {
                if (!TryParseCommandLine(File.ReadAllText(Path.Combine(dir, "cmdline")), out var id)) return null;
                var started = ParseStartTime(File.ReadAllText(Path.Combine(dir, "stat")), bootTime);
                return started is null ? null : new ShimProcess(pid, id, started.Value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null; // The process ended (or is not ours to read) while we looked.
            }
        }

        private static DateTimeOffset? ReadBootTime(string procRoot)
        {
            try
            {
                foreach (var line in File.ReadLines(Path.Combine(procRoot, "stat")))
                {
                    if (line.StartsWith("btime ", StringComparison.Ordinal)
                        && long.TryParse(line.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(seconds);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null; // /proc/stat unreadable: no start times, so nothing is judged.
            }
            return null;
        }
    }
}
