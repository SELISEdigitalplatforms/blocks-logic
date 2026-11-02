using System.Globalization;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>The <c>/proc/meminfo</c> figures the recorder keeps, in kB as the kernel reports them.</summary>
    public sealed record MemInfo(long TotalKb, long AvailableKb, long AnonKb, long CacheKb, long SwapTotalKb, long SwapFreeKb)
    {
        /// <summary>Parses the lines of <c>/proc/meminfo</c>. Missing keys read as 0.</summary>
        public static MemInfo Parse(IEnumerable<string> lines)
        {
            var values = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0) continue;
                var digits = new string(line[(colon + 1)..].Where(char.IsDigit).ToArray());
                if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var kb))
                {
                    values[line[..colon]] = kb;
                }
            }

            long Get(string key) => values.GetValueOrDefault(key);
            // Page cache the kernel gives back under pressure: Cached plus reclaimable slab.
            return new MemInfo(
                Get("MemTotal"), Get("MemAvailable"), Get("AnonPages"),
                Get("Cached") + Get("SReclaimable"), Get("SwapTotal"), Get("SwapFree"));
        }
    }

    /// <summary>The aggregate <c>cpu</c> line of <c>/proc/stat</c>: busy and total jiffies.</summary>
    public readonly record struct CpuTimes(long Total, long Idle)
    {
        /// <summary>Parses <c>cpu  user nice system idle iowait irq softirq steal ...</c>; null when it is not that line.</summary>
        public static CpuTimes? Parse(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || parts[0] != "cpu") return null;

            long total = 0;
            long idle = 0;
            // Fields 1-8 only: guest time is already counted inside user and nice.
            for (var i = 1; i < Math.Min(parts.Length, 9); i++)
            {
                if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var jiffies)) return null;
                total += jiffies;
                if (i is 4 or 5) idle += jiffies; // idle + iowait
            }
            return new CpuTimes(total, idle);
        }

        /// <summary>Share of time spent busy between two readings; null when no time passed.</summary>
        public static double? Percent(CpuTimes before, CpuTimes after)
        {
            var total = after.Total - before.Total;
            if (total <= 0) return null;
            var busy = total - (after.Idle - before.Idle);
            return Math.Clamp(100.0 * busy / total, 0, 100);
        }
    }
}
