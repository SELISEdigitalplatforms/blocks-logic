using System.Globalization;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>One threshold a sample is over: a stable key, a short message and the numbers.</summary>
    public sealed record Breach(string Key, string Message, double? Value, double? Threshold);

    /// <summary>
    /// Which thresholds a reading is over. Pure functions of the numbers, so the recorder and
    /// <c>fnctl police</c> (see <see cref="HealthThresholds"/>) judge the same way.
    /// </summary>
    public static class HealthRules
    {
        /// <summary>Memory, swap, CPU and disk. A null reading never counts as a breach.</summary>
        public static IEnumerable<Breach> HostBreaches(HostStats host)
        {
            if (host.MemTotalMb is > 0 && host.MemAvailableMb is { } avail)
            {
                var percent = 100.0 * avail / host.MemTotalMb.Value;
                if (percent < HealthThresholds.MemAvailableMinPercent)
                {
                    yield return new Breach("memory", $"Only {Fmt(percent)}% of memory is available ({avail} MB)",
                        percent, HealthThresholds.MemAvailableMinPercent);
                }
            }

            if (host.SwapTotalMb is > 0 && host.SwapUsedMb is { } swapUsed)
            {
                var percent = 100.0 * swapUsed / host.SwapTotalMb.Value;
                if (percent > HealthThresholds.SwapUsedMaxPercent)
                {
                    yield return new Breach("swap", $"Swap is {Fmt(percent)}% used ({swapUsed} MB)",
                        percent, HealthThresholds.SwapUsedMaxPercent);
                }
            }

            if (host.CpuPercent is { } cpu && cpu > HealthThresholds.CpuMaxPercent)
            {
                yield return new Breach("cpu", $"CPU is {Fmt(cpu)}% busy", cpu, HealthThresholds.CpuMaxPercent);
            }

            if (host.DiskPercent is { } disk && disk > HealthThresholds.DiskMaxPercent)
            {
                yield return new Breach("disk", $"Docker disk is {Fmt(disk)}% full", disk, HealthThresholds.DiskMaxPercent);
            }
        }

        /// <summary>Everything the recorder raises events for.</summary>
        public static IReadOnlyList<Breach> Evaluate(MetricsSample sample)
        {
            var breaches = HostBreaches(sample.Host).ToList();

            if (sample.Docker.OrphanShims is > 0 and var orphans)
            {
                breaches.Add(new Breach("orphan-shims", $"{orphans} containerd-shim process(es) belong to no container",
                    orphans, 0));
            }

            foreach (var (stream, reading) in sample.Streams)
            {
                if (reading.Lag is { } lag && lag > HealthThresholds.StreamLagMax)
                {
                    breaches.Add(new Breach($"lag-{stream}", $"Stream '{stream}' is {lag} entries behind",
                        lag, HealthThresholds.StreamLagMax));
                }
            }

            if (!sample.Healthy) breaches.Add(new Breach("unhealthy", "The runner reports itself not healthy", null, null));

            return breaches;
        }

        private static string Fmt(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Keeps a breach that lasts from flooding the event file: it is raised when the sample first
    /// goes bad, then again at most every <see cref="HealthThresholds.EventReminder"/> while it
    /// stays bad, and forgotten as soon as it clears so the next breach is new again.
    /// </summary>
    public sealed class EventGate
    {
        private readonly Dictionary<string, DateTimeOffset> _raisedAt = [];

        /// <summary>The breaches to write now, given every breach present in this sample.</summary>
        public IReadOnlyList<Breach> Admit(IReadOnlyList<Breach> present, DateTimeOffset now)
        {
            var keys = present.Select(b => b.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var cleared in _raisedAt.Keys.Where(k => !keys.Contains(k)).ToList())
            {
                _raisedAt.Remove(cleared);
            }

            var admitted = new List<Breach>();
            foreach (var breach in present)
            {
                if (_raisedAt.TryGetValue(breach.Key, out var last) && now - last < HealthThresholds.EventReminder)
                {
                    continue;
                }
                _raisedAt[breach.Key] = now;
                admitted.Add(breach);
            }
            return admitted;
        }
    }
}
