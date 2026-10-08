using System.Diagnostics;
using System.Globalization;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// A container's CPU time as the host kernel charges it: cgroup v2 <c>cpu.stat</c>
    /// <c>usage_usec</c>. This is the counter the CPU limit is enforced against, so it is the only
    /// one a figure compared with the limit may come from. The runtime's own
    /// <c>process.cpuUsage()</c> is not: under gVisor it counts throttled time as used (measured
    /// 2026-10-06, 480 ms "used" for 110 ms charged).
    /// </summary>
    public static class HostCpu
    {
        /// <summary>Microseconds used so far, read on the spot; null when the cgroup cannot be read (gone, other layout, no access).</summary>
        public static long? ReadMicroseconds(string? containerId)
        {
            if (string.IsNullOrEmpty(containerId)) return null;
            // systemd cgroup driver (this host), then the cgroupfs driver's layout.
            foreach (var path in (string[])[
                $"/sys/fs/cgroup/system.slice/docker-{containerId}.scope/cpu.stat",
                $"/sys/fs/cgroup/docker/{containerId}/cpu.stat"])
            {
                try
                {
                    foreach (var line in File.ReadLines(path))
                    {
                        if (line.StartsWith("usage_usec ", StringComparison.Ordinal)
                            && long.TryParse(line.AsSpan(11), NumberStyles.None, CultureInfo.InvariantCulture, out var usec))
                        {
                            return usec;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Not this layout, or not readable: try the next, else null.
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The CPU a single run's handler used, over the handler's own window: the host counter read
    /// when the <c>started</c> line arrives and again when the <c>result</c> line does. The
    /// container's total is not that figure: it holds gVisor booting and Node loading at the
    /// start-up boost (1 CPU), which made a trivial handler show "243 / 100 m" (2026-10-08).
    /// <para>
    /// Output is only watched. A marker inside a log message is JSON-escaped and does not match;
    /// one a package writes raw only moves this run's own figure, and a result before the start,
    /// or a counter that went backwards, gives no window at all.
    /// </para>
    /// </summary>
    internal sealed class HandlerCpuWindow(Func<long?> readMicroseconds, Stopwatch clock)
    {
        private const string StartedMarker = "\"t\":\"started\"";
        private const string ResultMarker = "\"t\":\"result\"";

        private string _tail = string.Empty;   // a marker split across two reads
        private (long Ms, long? Usec)? _start;
        private (long Ms, long? Usec)? _end;

        /// <summary>Fed each decoded piece of output, on the reader's thread only.</summary>
        public void OnText(string text)
        {
            if (_end is not null) return;
            var window = _tail + text;
            if (_start is null)
            {
                var at = window.IndexOf(StartedMarker, StringComparison.Ordinal);
                if (at < 0)
                {
                    _tail = Tail(window);
                    return;
                }
                _start = (clock.ElapsedMilliseconds, readMicroseconds());
                window = window[(at + StartedMarker.Length)..];
            }
            if (window.Contains(ResultMarker, StringComparison.Ordinal))
            {
                _end = (clock.ElapsedMilliseconds, readMicroseconds());
                return;
            }
            _tail = Tail(window);
        }

        /// <summary>CPU ms (at least 1) and the wall ms it covers; null when either reading is missing or makes no sense.</summary>
        public (long CpuMs, long WindowMs)? Measured()
        {
            if (_start is not { Usec: { } from } start || _end is not { Usec: { } to } end) return null;
            if (to < from || end.Ms < start.Ms) return null;
            return (Math.Max(1, (to - from + 500) / 1000), end.Ms - start.Ms);
        }

        private static string Tail(string window) =>
            window.Length > StartedMarker.Length ? window[^StartedMarker.Length..] : window;   // the longer marker
    }
}
